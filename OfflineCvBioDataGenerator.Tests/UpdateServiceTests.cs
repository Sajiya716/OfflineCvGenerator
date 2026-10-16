using System;
using OfflineCvBioDataGenerator.Services;
using Xunit;

namespace OfflineCvBioDataGenerator.Tests
{
    /// <summary>
    /// The update check runs on the UI thread during startup, so it must be impossible for a
    /// missing/placeholder configuration or a network problem to throw. These tests stay
    /// offline: they only exercise the "not configured" path.
    /// </summary>
    public class UpdateServiceTests : IDisposable
    {
        public void Dispose() => UpdateService.UseDefaultFeedUrl();

        [Fact]
        public void IsConfigured_IsFalseForTheShippedPlaceholder()
        {
            UpdateService.UseDefaultFeedUrl();

            Assert.False(UpdateService.IsConfigured);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void IsConfigured_IsFalseWhenThereIsNoUrl(string? url)
        {
            UpdateService.UseFeedUrl(url);

            Assert.False(UpdateService.IsConfigured);
        }

        [Fact]
        public void IsConfigured_IsTrueForARealUrl()
        {
            UpdateService.UseFeedUrl("https://example.com/update.xml");

            Assert.True(UpdateService.IsConfigured);
        }

        [Fact]
        public void CheckForUpdatesOnStartup_WithNoFeedConfigured_DoesNotThrow()
        {
            UpdateService.UseFeedUrl(string.Empty);

            Exception? error = Record.Exception(() => UpdateService.CheckForUpdatesOnStartup(null));

            Assert.Null(error);
        }

        [Fact]
        public void CheckForUpdatesNow_WithNoFeedConfigured_DoesNotThrow()
        {
            UpdateService.UseFeedUrl(string.Empty);

            Exception? error = Record.Exception(() => UpdateService.CheckForUpdatesNow(null));

            Assert.Null(error);
        }
    }
}
