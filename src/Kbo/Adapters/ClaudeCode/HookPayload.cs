namespace Kbo.Adapters.ClaudeCode;

/// <summary>
/// Claude Code hook payload contract: JSON keys, hook event names, tool names,
/// and the context.loaded kind labels this adapter assigns (ADR-0006).
/// </summary>
internal static class HookPayload
{
    public const string SessionId = "session_id";
    public const string Cwd = "cwd";
    public const string HookEventName = "hook_event_name";
    public const string ToolName = "tool_name";
    public const string ToolInput = "tool_input";
    public const string ToolResponse = "tool_response";
    public const string FilePath = "file_path";
    public const string NotebookPath = "notebook_path";
    public const string Pattern = "pattern";
    public const string Path = "path";
    public const string Skill = "skill";
    public const string Content = "content";
    public const string OldString = "old_string";
    public const string NewString = "new_string";
    public const string NewSource = "new_source";
    public const string SizeSuffix = "_size";

    internal static class Events
    {
        public const string PostToolUse = "PostToolUse";
        public const string SessionStart = "SessionStart";
    }

    internal static class Tools
    {
        public const string Read = "Read";
        public const string Grep = "Grep";
        public const string Glob = "Glob";
        public const string Write = "Write";
        public const string Edit = "Edit";
        public const string NotebookEdit = "NotebookEdit";
        public const string Skill = "Skill";
    }

    internal static class ContextKinds
    {
        public const string GlobalInstructions = "global-instructions";
        public const string ProjectInstructions = "project-instructions";
        public const string Rules = "rules";
        public const string Memory = "memory";
    }
}
