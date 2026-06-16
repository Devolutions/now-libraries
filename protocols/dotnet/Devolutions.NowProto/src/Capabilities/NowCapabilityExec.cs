namespace Devolutions.NowProto.Capabilities
{
    /// <summary>
    /// NOW-PROTO: NOW_CHANNEL_CAPSET_MSG execCapset field.
    /// </summary>
    [Flags]
    public enum NowCapabilityExec : ushort
    {
        /// <summary>
        /// Empty capability set.
        /// </summary>
        None = 0x0000,
        /// <summary>
        /// Generic "Run" execution style.
        ///
        /// NOW-PROTO: NOW_CAP_EXEC_STYLE_RUN
        /// </summary>
        Run = 0x0001,
        /// <summary>
        /// CreateProcess() execution style.
        ///
        /// NOW-PROTO: NOW_CAP_EXEC_STYLE_PROCESS
        /// </summary>
        Process = 0x0002,
        /// <summary>
        /// System shell (.sh) execution style.
        ///
        /// NOW-PROTO: NOW_CAP_EXEC_STYLE_SHELL
        /// </summary>
        Shell = 0x0004,
        /// <summary>
        /// Windows batch file (.bat) execution style.
        ///
        /// NOW-PROTO: NOW_CAP_EXEC_STYLE_BATCH
        /// </summary>
        Batch = 0x0008,
        /// <summary>
        /// Windows PowerShell (.ps1) execution style.
        ///
        /// NOW-PROTO: NOW_CAP_EXEC_STYLE_WINPS
        /// </summary>
        WinPs = 0x0010,
        /// <summary>
        /// PowerShell 7 (.ps1) execution style.
        ///
        /// NOW-PROTO: NOW_CAP_EXEC_STYLE_PWSH
        /// </summary>
        Pwsh = 0x0020,

        /// <summary>
        /// Set if host implements exec session IO redirection.
        ///
        /// NOW-PROTO: NOW_CAP_EXEC_IO_REDIRECTION
        /// </summary>
        IoRedirection = 0x1000,

        /// <summary>
        /// Set if host supports encoding control flags for exec commands
        /// (RAW_ENCODING, UNICODE_CONSOLE, and ENCODING_UTF8 flags).
        ///
        /// NOW-PROTO: NOW_CAP_EXEC_UNICODE_CONSOLE
        /// </summary>
        UnicodeConsole = 0x0040,

        All = Run | Process | Shell | Batch | WinPs | Pwsh | IoRedirection | UnicodeConsole,
    }
}