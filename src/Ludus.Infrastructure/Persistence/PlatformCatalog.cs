using Ludus.Domain.Common;
using Ludus.Domain.Library;

namespace Ludus.Infrastructure.Persistence;

internal static class PlatformCatalog
{
    public static IReadOnlyList<Platform> Create() =>
    [
        Create("Nintendo Entertainment System", "nes", [".nes", ".unf", ".unif", ".zip", ".7z"], "#ef4444"),
        Create("Super Nintendo", "snes", [".sfc", ".smc", ".zip", ".7z"], "#a78bfa"),
        Create("Nintendo 64", "n64", [".z64", ".n64", ".v64", ".zip", ".7z"], "#22c55e"),
        Create("Nintendo GameCube", "gamecube", [".rvz", ".iso", ".gcm", ".ciso", ".zip", ".7z"], "#818cf8"),
        Create("Nintendo Wii", "wii", [".rvz", ".iso", ".wbfs", ".wad", ".zip", ".7z"], "#38bdf8", categories: [1000, 1030, 1060]),
        Create("Nintendo Wii U", "wii-u", [".wud", ".wux", ".wua", ".rpx", ".zip", ".7z"], "#06b6d4", folders: true),
        Create("Game Boy", "gb", [".gb", ".zip", ".7z"], "#84cc16"),
        Create("Game Boy Color", "gbc", [".gbc", ".gb", ".zip", ".7z"], "#eab308"),
        Create("Game Boy Advance", "gba", [".gba", ".zip", ".7z"], "#6366f1"),
        Create("Nintendo DS", "nds", [".nds", ".zip", ".7z"], "#94a3b8", categories: [1000, 1010]),
        Create("Nintendo 3DS", "3ds", [".3ds", ".cia", ".cci", ".cxi", ".zip", ".7z"], "#f43f5e"),
        Create("PlayStation", "ps1", [".chd", ".cue", ".bin", ".iso", ".pbp", ".m3u", ".zip", ".7z"], "#60a5fa"),
        Create("PlayStation 2", "ps2", [".chd", ".iso", ".cue", ".bin", ".cso", ".m3u", ".zip", ".7z"], "#3b82f6"),
        Create("PlayStation 3", "ps3", [".iso", ".pkg", ".zip", ".7z"], "#2563eb", folders: true, categories: [1000, 1080]),
        Create("PlayStation Portable", "psp", [".iso", ".cso", ".chd", ".pbp", ".zip", ".7z"], "#1d4ed8", categories: [1000, 1020]),
        Create("Sega Mega Drive / Genesis", "mega-drive", [".md", ".gen", ".bin", ".smd", ".zip", ".7z"], "#0ea5e9"),
        Create("Sega Dreamcast", "dreamcast", [".chd", ".gdi", ".cdi", ".bin", ".raw", ".m3u", ".zip", ".7z"], "#f97316"),
        Create("Commodore Amiga", "amiga", [".adf", ".adz", ".hdf", ".ipf", ".lha", ".lzh", ".zip", ".7z"], "#fb7185", MetadataProviderKind.ScreenScraper),
        Create("Commodore 64", "c64", [".d64", ".d81", ".t64", ".tap", ".prg", ".crt", ".zip", ".7z"], "#a16207", MetadataProviderKind.ScreenScraper),
        Create("DOS", "dos", [".zip", ".7z", ".iso", ".cue", ".bin", ".exe", ".com"], "#c084fc", MetadataProviderKind.ScreenScraper, folders: true),
        Create("Arcade", "arcade", [".zip", ".7z", ".chd"], "#facc15", MetadataProviderKind.ScreenScraper),
        Create("Nintendo Switch", "switch", [".nsp", ".nsz", ".xci", ".xcz", ".zip", ".7z"], "#ef4444")
    ];

    private static Platform Create(string name, string slug, List<string> extensions, string color,
        MetadataProviderKind provider = MetadataProviderKind.Igdb, bool folders = false, List<int>? categories = null) => new()
        {
            Name = name,
            Slug = slug,
            AllowedExtensions = extensions,
            AccentColor = color,
            PrimaryMetadataProvider = provider,
            SupportsFolderGames = folders,
            TorznabCategories = categories ?? [1000]
        };
}
