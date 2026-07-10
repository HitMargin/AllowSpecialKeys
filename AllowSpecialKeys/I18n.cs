namespace AllowSpecialKeys;

internal static class I18n
{
    // Index: 0=中文, 1=English, 2=한국어
    private static readonly string[][] Texts = new[]
    {
        /* 0-中文 */ new[] {
            "允许特殊按键",
            "=== 系统级按键拦截 ===",
            "拦截 Windows 键（Win → 开始菜单）",
            "拦截 Alt+Tab / Alt+F4 / Alt+Esc",
            "拦截 Alt+Enter（全屏切换）",
            "拦截 Ctrl+Esc",
            "=== 游戏内 ===",
            "允许特殊键作为游戏按键（Win/Alt/Ctrl... 游戏中可用）",
            "允许 F12 作为游戏按键",
            "应用",
            "中文", "English", "한국어",
        },
        /* 1-English */ new[] {
            "Allow Special Keys",
            "=== OS-Level Key Blocker ===",
            "Block Windows key (Win → Start menu)",
            "Block Alt+Tab / Alt+F4 / Alt+Esc",
            "Block Alt+Enter (fullscreen toggle)",
            "Block Ctrl+Esc",
            "=== Gameplay ===",
            "Allow special keys as gameplay keys (Win/Alt/Ctrl... in-game)",
            "Allow F12 as gameplay key",
            "Apply",
            "中文", "English", "한국어",
        },
        /* 2-한국어 */ new[] {
            "특수 키 허용",
            "=== 시스템 키 차단 ===",
            "Windows 키 차단 (Win → 시작 메뉴)",
            "Alt+Tab / Alt+F4 / Alt+Esc 차단",
            "Alt+Enter 차단 (전체화면 전환)",
            "Ctrl+Esc 차단",
            "=== 게임플레이 ===",
            "특수 키를 게임 키로 허용 (Win/Alt/Ctrl...)",
            "F12를 게임 키로 허용",
            "적용",
            "中文", "English", "한국어",
        },
    };

    public const int T_TITLE        = 0;
    public const int T_OS_HEADER    = 1;
    public const int T_BLOCK_WIN    = 2;
    public const int T_BLOCK_ALT_TAB = 3;
    public const int T_BLOCK_ALT_ENTER = 4;
    public const int T_BLOCK_CTRL_ESC = 5;
    public const int T_GAME_HEADER  = 6;
    public const int T_SPECIAL_KEYS = 7;
    public const int T_F12_KEY      = 8;
    public const int T_APPLY        = 9;
    public const int T_LANG_ZH      = 10;
    public const int T_LANG_EN      = 11;
    public const int T_LANG_KO      = 12;

    public static string Get(int lang, int key) => Texts[lang][key];
}
