namespace Kbo.Adapters.ClaudeCode;

/// <summary>
/// Claude Code hook payload contract: JSON keys, hook event names, tool names,
/// and the context.loaded kind labels this adapter assigns (ADR-0006).
/// </summary>
public static class HookPayload
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

    public static class Events
    {
        public const string PostToolUse = "PostToolUse";
        public const string SessionStart = "SessionStart";
    }

    public static class Tools
    {
        public const string Read = "Read";
        public const string Grep = "Grep";
        public const string Glob = "Glob";
        public const string Write = "Write";
        public const string Edit = "Edit";
        public const string NotebookEdit = "NotebookEdit";
        public const string Skill = "Skill";
    }

    public static class ContextKinds
    {
        public const string GlobalInstructions = "global-instructions";
        public const string ProjectInstructions = "project-instructions";
        public const string Rules = "rules";
        public const string Memory = "memory";
    }
}
