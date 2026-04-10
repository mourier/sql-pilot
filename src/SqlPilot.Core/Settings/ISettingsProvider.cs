namespace SqlPilot.Core.Settings
{
    public interface ISettingsProvider
    {
        SqlPilotSettings GetSettings();
        void SaveSettings(SqlPilotSettings settings);
    }
}
