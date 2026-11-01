namespace SnapBar.App;

internal interface ISettingsDraft
{
    string DraftName { get; }
    bool IsDirty { get; }
    bool TrySave();
    void Discard();
    void Reveal();
}

internal enum SettingsCategory { General, Actions, AiCommands, AiServices, Compatibility, About }
internal enum ExitChoice { Save, Discard, Cancel }
