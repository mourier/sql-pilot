namespace SqlPilot.UI.Controls
{
    /// <summary>
    /// Action identifiers passed through <see cref="SearchControl.ActionRequested"/>.
    /// Shared between the UI layer (which fires them) and the Package layer (which dispatches them).
    /// </summary>
    public static class SearchActions
    {
        /// <summary>Type-dependent default action — what Enter/double-click does.</summary>
        public const string Default = "Default";

        /// <summary>Type-dependent secondary action — what Right-arrow does. Matches hunting-dog: Edit Data for tables, Execute for procs/functions.</summary>
        public const string Secondary = "Secondary";

        public const string SelectTop = "SelectTop";
        public const string EditData = "EditData";
        public const string DesignTable = "DesignTable";
        public const string ScriptCreate = "ScriptCreate";
        public const string ScriptAlter = "ScriptAlter";
        public const string Execute = "Execute";

        /// <summary>Handled locally inside SearchControl — not routed to the Package.</summary>
        public const string ToggleFavorite = "ToggleFavorite";
    }
}
