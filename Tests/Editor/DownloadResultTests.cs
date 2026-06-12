using System;
using System.Threading.Tasks;
using NUnit.Framework;

namespace KidzDev.AddressablesToolkit.Tests
{
    public class DownloadResultTests
    {
        [Test]
        public void FromException_Cancellation_IsCancelled()
        {
            Assert.That(DownloadResult.FromException(new OperationCanceledException()).Outcome,
                Is.EqualTo(DownloadOutcome.Cancelled));
            Assert.That(DownloadResult.FromException(new TaskCanceledException()).Outcome,
                Is.EqualTo(DownloadOutcome.Cancelled));
        }

        [TestCase("HTTP/1.1 403 Forbidden", DownloadOutcome.NotFound)]
        [TestCase("HTTP/1.1 404 Not Found", DownloadOutcome.NotFound)]
        [TestCase("RemoteProviderException: catalog Not Found on server", DownloadOutcome.NotFound)]
        [TestCase("Cannot connect to destination host", DownloadOutcome.NoInternet)]
        [TestCase("Failed to resolve host name", DownloadOutcome.NoInternet)]
        [TestCase("Unable to write data to the transport connection", DownloadOutcome.NoDiskSpace)]
        [TestCase("No space left on device", DownloadOutcome.NoDiskSpace)]
        [TestCase("Something completely different went wrong", DownloadOutcome.Error)]
        public void FromException_ClassifiesByMessage(string message, DownloadOutcome expected)
        {
            var result = DownloadResult.FromException(new Exception(message));
            Assert.That(result.Outcome, Is.EqualTo(expected));
            Assert.That(result.Message, Is.EqualTo(message));
        }

        [Test]
        public void FromException_NullException_IsError()
        {
            Assert.That(DownloadResult.FromException(null).Outcome, Is.EqualTo(DownloadOutcome.Error));
        }

        [Test]
        public void IsSuccess_OnlyForSuccessAndNoUpdate()
        {
            Assert.That(DownloadResult.NoUpdate().IsSuccess, Is.True);
            Assert.That(DownloadResult.Success(42).IsSuccess, Is.True);
            Assert.That(DownloadResult.Rejected().IsSuccess, Is.False);
            Assert.That(DownloadResult.Cancelled().IsSuccess, Is.False);
            Assert.That(new DownloadResult(DownloadOutcome.Error).IsSuccess, Is.False);
        }

        [Test]
        public void Success_CarriesByteCount()
        {
            Assert.That(DownloadResult.Success(1234).Bytes, Is.EqualTo(1234));
        }
    }
}
