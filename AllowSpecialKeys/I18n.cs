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
            "允许特殊键作为游戏按键（总开关）",
            "允许 Win 键",
            "允许 Tab 键",
            "允许 Enter 键",
            "允许 F4 键",
            "允许 F12 键",
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
            "Allow special keys as gameplay keys (master)",
            "Allow Win key",
            "Allow Tab key",
            "Allow Enter key",
            "Allow F4 key",
            "Allow F12 key",
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
            "특수 키를 게임 키로 허용 (마스터)",
            "Win 키 허용",
            "Tab 키 허용",
            "Enter 키 허용",
            "F4 키 허용",
            "F12 키 허용",
            "적용",
            "中文", "English", "한국어",
        },
    };

    public const int T_TITLE = 0;
    public const int T_OS_HEADER = 1;
    public const int T_BLOCK_WIN = 2;
    public const int T_BLOCK_ALT_TAB = 3;
    public const int T_BLOCK_ALT_ENTER = 4;
    public const int T_BLOCK_CTRL_ESC = 5;
    public const int T_GAME_HEADER = 6;
    public const int T_SPECIAL_KEYS = 7;
    public const int T_ALLOW_WIN = 8;
    public const int T_ALLOW_TAB = 9;
    public const int T_ALLOW_ENTER = 10;
    public const int T_ALLOW_F4 = 11;
    public const int T_ALLOW_F12 = 12;
    public const int T_APPLY = 13;
    public const int T_LANG_ZH = 14;
    public const int T_LANG_EN = 15;
    public const int T_LANG_KO = 16;

    public static string Get(int lang, int key) => Texts[lang][key];
}