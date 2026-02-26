using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace SimpleIPScanner.Services
{
    public class UpdateService
    {
        private const string GitHubRepo = "ChaseCorbin/SimpleIPScanner";
        private readonly UpdateManager _manager;
        private UpdateInfo? _pendingUpdate;

        public UpdateService()
        {
            _manager = new UpdateManager(new GithubSource(
                repoUrl: $"https://github.com/{GitHubRepo}",
                accessToken: null,
                prerelease: false
            ));
        }

        /// <summary>
        /// Silently checks GitHub for a newer release. Returns the new version
        /// string if one is available, otherwise null. Never throws.
        /// </summary>
        public async Task<string?> CheckForUpdateAsync()
        {
            try
            {
                // IsInstalled is false when running from a dev build or a plain
                // portable exe that was not installed via a Velopack package.
                // Skip the check so development runs work without errors.
                if (!_manager.IsInstalled)
                    return null;

                _pendingUpdate = await _manager.CheckForUpdatesAsync();
                return _pendingUpdate?.TargetFullRelease.Version.ToString();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Downloads the pending update and restarts into the new version.
        /// Call only after CheckForUpdateAsync returned a non-null version.
        /// Never throws — silently does nothing if the download fails.
        /// </summary>
        public async Task ApplyUpdateAndRestartAsync()
        {
            if (_pendingUpdate == null)
                return;

            try
            {
                await _manager.DownloadUpdatesAsync(_pendingUpdate);
                _manager.ApplyUpdatesAndRestart(_pendingUpdate);
                // ApplyUpdatesAndRestart exits the process — code below never runs
                // unless an exception is thrown before the restart.
            }
            catch { }
        }
    }
}
