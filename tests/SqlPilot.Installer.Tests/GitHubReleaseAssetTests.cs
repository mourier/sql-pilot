using System.Linq;
using FluentAssertions;
using SqlPilot.Installer.Services;
using Xunit;

namespace SqlPilot.Installer.Tests
{
    /// <summary>
    /// Guards the release-asset parsing the one-click installer depends on. If this
    /// returns nothing the installer can't download anything, and it fails with
    /// "Release vX has no SqlPilot ZIP asset attached" — which is what shipped.
    /// </summary>
    public class GitHubReleaseAssetTests
    {
        /// <summary>
        /// Shaped like a real GitHub releases payload. The load-bearing detail is the
        /// uploader login "github-actions[bot]": every release built by the workflow has
        /// it, and the "]" inside that string used to end the assets array early.
        /// </summary>
        private const string ReleaseJson = @"{
          ""url"": ""https://api.github.com/repos/mourier/sql-pilot/releases/1"",
          ""assets_url"": ""https://api.github.com/repos/mourier/sql-pilot/releases/1/assets"",
          ""html_url"": ""https://github.com/mourier/sql-pilot/releases/tag/v1.0.0"",
          ""tag_name"": ""v1.0.0"",
          ""assets"": [
            {
              ""url"": ""https://api.github.com/repos/mourier/sql-pilot/releases/assets/1"",
              ""id"": 1,
              ""name"": ""SqlPilot-v1.0.0.zip"",
              ""label"": """",
              ""uploader"": { ""login"": ""github-actions[bot]"", ""id"": 41898282, ""type"": ""Bot"" },
              ""content_type"": ""application/zip"",
              ""state"": ""uploaded"",
              ""size"": 492544,
              ""browser_download_url"": ""https://github.com/mourier/sql-pilot/releases/download/v1.0.0/SqlPilot-v1.0.0.zip""
            },
            {
              ""url"": ""https://api.github.com/repos/mourier/sql-pilot/releases/assets/2"",
              ""id"": 2,
              ""name"": ""SqlPilotInstaller-v1.0.0.zip"",
              ""label"": """",
              ""uploader"": { ""login"": ""github-actions[bot]"", ""id"": 41898282, ""type"": ""Bot"" },
              ""content_type"": ""application/zip"",
              ""state"": ""uploaded"",
              ""size"": 376832,
              ""browser_download_url"": ""https://github.com/mourier/sql-pilot/releases/download/v1.0.0/SqlPilotInstaller-v1.0.0.zip""
            }
          ],
          ""body"": ""Release notes [with a bracket] and a } brace.""
        }";

        [Fact]
        public void ExtractAssets_ReturnsEveryAsset_WhenTheUploaderLoginContainsABracket()
        {
            var assets = GitHubReleaseClient.ExtractAssets(ReleaseJson);

            assets.Select(a => a.Name).Should().Equal(
                "SqlPilot-v1.0.0.zip",
                "SqlPilotInstaller-v1.0.0.zip");
        }

        [Fact]
        public void ExtractAssets_KeepsTheDownloadUrlAndSizeOfEachAsset()
        {
            var payload = GitHubReleaseClient.ExtractAssets(ReleaseJson)
                .Single(a => a.Name == "SqlPilot-v1.0.0.zip");

            payload.BrowserDownloadUrl.Should().Be(
                "https://github.com/mourier/sql-pilot/releases/download/v1.0.0/SqlPilot-v1.0.0.zip");
            payload.Size.Should().Be(492544);
        }

        [Fact]
        public void ExtractAssets_FindsThePayloadZip_NotTheInstallerZipOrTheVsix()
        {
            // Mirrors InstallEngine's selection: the "SqlPilot-" prefix excludes the
            // installer's own ZIP, and the ".zip" suffix excludes the gallery .vsix,
            // which sorts first in a real v1.1.1 payload.
            const string withVsix = @"{ ""tag_name"": ""v1.1.1"", ""assets"": [
                { ""name"": ""SqlPilot-v1.1.1.vsix"", ""size"": 1, ""uploader"": { ""login"": ""github-actions[bot]"" },
                  ""browser_download_url"": ""https://example.invalid/SqlPilot-v1.1.1.vsix"" },
                { ""name"": ""SqlPilot-v1.1.1.zip"", ""size"": 2, ""uploader"": { ""login"": ""github-actions[bot]"" },
                  ""browser_download_url"": ""https://example.invalid/SqlPilot-v1.1.1.zip"" },
                { ""name"": ""SqlPilotInstaller-v1.1.1.zip"", ""size"": 3, ""uploader"": { ""login"": ""github-actions[bot]"" },
                  ""browser_download_url"": ""https://example.invalid/SqlPilotInstaller-v1.1.1.zip"" } ] }";

            var picked = GitHubReleaseClient.ExtractAssets(withVsix)
                .FirstOrDefault(a => a.Name.StartsWith("SqlPilot-") && a.Name.EndsWith(".zip"));

            picked.Should().NotBeNull();
            picked.Name.Should().Be("SqlPilot-v1.1.1.zip");
        }

        [Fact]
        public void ExtractAssets_ReturnsEmpty_WhenTheReleaseHasNoAssets()
        {
            GitHubReleaseClient.ExtractAssets(@"{ ""tag_name"": ""v9.9.9"", ""assets"": [] }")
                .Should().BeEmpty();
        }

        [Fact]
        public void ExtractAssets_StopsAtTheEndOfTheAssetsArray()
        {
            // A later array of objects in the payload must not be read as more assets.
            const string trailing = @"{ ""assets"": [
                { ""name"": ""SqlPilot-v1.0.0.zip"", ""size"": 1, ""uploader"": { ""login"": ""github-actions[bot]"" },
                  ""browser_download_url"": ""https://example.invalid/a.zip"" } ],
              ""reactions"": [ { ""name"": ""not-an-asset.zip"", ""browser_download_url"": ""https://example.invalid/b.zip"" } ] }";

            GitHubReleaseClient.ExtractAssets(trailing)
                .Select(a => a.Name).Should().Equal("SqlPilot-v1.0.0.zip");
        }
    }
}
