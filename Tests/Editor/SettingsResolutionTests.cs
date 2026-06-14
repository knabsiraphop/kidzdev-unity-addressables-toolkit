using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KidzDev.Unity.AddressablesToolkit.Tests
{
    public class SettingsResolutionTests
    {
        private AddressablesToolkitSettings _settings;

        [SetUp]
        public void SetUp()
        {
            _settings = ScriptableObject.CreateInstance<AddressablesToolkitSettings>();
        }

        [TearDown]
        public void TearDown()
        {
            AddressablesToolkitSettings.EnvironmentOverride = null;
            Environment.SetEnvironmentVariable("ADDRESSABLES_ENV", null);
            if (_settings != null) UnityEngine.Object.DestroyImmediate(_settings);
        }

        [Test]
        public void ResolveEnvironmentName_DefaultsToSerializedValue()
        {
            _settings.activeEnvironment = "production";
            Assert.That(_settings.ResolveEnvironmentName(), Is.EqualTo("production"));
        }

        [Test]
        public void ResolveEnvironmentName_EnvVarBeatsSerialized()
        {
            _settings.activeEnvironment = "production";
            Environment.SetEnvironmentVariable("ADDRESSABLES_ENV", "staging");
            Assert.That(_settings.ResolveEnvironmentName(), Is.EqualTo("staging"));
        }

        [Test]
        public void ResolveEnvironmentName_CodeOverrideBeatsEverything()
        {
            _settings.activeEnvironment = "production";
            Environment.SetEnvironmentVariable("ADDRESSABLES_ENV", "staging");
            AddressablesToolkitSettings.EnvironmentOverride = "dev";
            Assert.That(_settings.ResolveEnvironmentName(), Is.EqualTo("dev"));
        }

        [Test]
        public void ResolveEnvironment_MatchesCaseInsensitively()
        {
            _settings.activeEnvironment = "STAGING";
            Assert.That(_settings.ResolveEnvironment().Name, Is.EqualTo("staging"));
        }

        [Test]
        public void ResolveEnvironment_UnknownName_FallsBackToFirstWithWarning()
        {
            _settings.activeEnvironment = "does-not-exist";
            LogAssert.Expect(LogType.Warning, new Regex("not found"));
            Assert.That(_settings.ResolveEnvironment().Name, Is.EqualTo(_settings.environments[0].Name));
        }

        [Test]
        public void ResolveEnvironment_NoEnvironments_ReturnsNull()
        {
            _settings.environments = new List<RemoteEnvironment>();
            Assert.That(_settings.ResolveEnvironment(), Is.Null);
        }

        [Test]
        public void ResolveVersion_EmptyUsesApplicationVersion()
        {
            _settings.contentVersion = "";
            Assert.That(_settings.ResolveVersion(), Is.EqualTo(Application.version));
            _settings.contentVersion = "9.9.9";
            Assert.That(_settings.ResolveVersion(), Is.EqualTo("9.9.9"));
        }

        [Test]
        public void GetPreloadKeys_SkipsNullAndEmptyLabels()
        {
            _settings.preloadLabels = new List<string> { "core", "", null, "ui" };
            Assert.That(_settings.GetPreloadKeys(), Is.EqualTo(new object[] { "core", "ui" }));
        }

        [Test]
        public void OverrideInstance_WinsOverResourcesLookup()
        {
            try
            {
                AddressablesToolkitSettings.OverrideInstance(_settings);
                Assert.That(AddressablesToolkitSettings.Instance, Is.SameAs(_settings));
            }
            finally
            {
                AddressablesToolkitSettings.OverrideInstance(null);
            }
        }
    }
}
