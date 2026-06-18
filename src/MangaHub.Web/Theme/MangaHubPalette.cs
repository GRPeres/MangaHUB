using MudBlazor;

namespace MangaHub.Web.Theme;

public static class MangaHubPalette
{
    public static readonly MudTheme Theme = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#7C3AED",
            Secondary = "#A78BFA",
            Tertiary = "#6D28D9",
            Background = "#FAF8FF",
            Surface = "#FFFFFF",
            AppbarBackground = "#FFFFFF",
            AppbarText = "#27113F",
            DrawerBackground = "#FFFFFF",
            DrawerText = "#3B2654",
            TextPrimary = "#241336",
            TextSecondary = "#6B5A7E",
            ActionDefault = "#6D28D9",
            Divider = "#E9DDFD",
            LinesDefault = "#E9DDFD",
            TableLines = "#E9DDFD",
            Info = "#8B5CF6",
            Success = "#7E57C2",
            Warning = "#A855F7",
            Error = "#9333EA"
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#C4B5FD",
            Secondary = "#A78BFA",
            Tertiary = "#DDD6FE",
            Background = "#10081B",
            Surface = "#1A1028",
            AppbarBackground = "#160D22",
            AppbarText = "#F5F0FF",
            DrawerBackground = "#160D22",
            DrawerText = "#EDE5FF",
            TextPrimary = "#F7F2FF",
            TextSecondary = "#C8B6DD",
            ActionDefault = "#C4B5FD",
            Divider = "#352148",
            LinesDefault = "#352148",
            TableLines = "#352148",
            Info = "#C4B5FD",
            Success = "#B794F4",
            Warning = "#D8B4FE",
            Error = "#E879F9"
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "8px",
            DrawerWidthLeft = "260px",
            AppbarHeight = "64px"
        },
        Typography = new Typography
        {
            Default = new DefaultTypography
            {
                FontFamily = ["Inter", "Segoe UI", "Arial", "sans-serif"],
                FontSize = ".95rem",
                LineHeight = "1.5"
            },
            H1 = new H1Typography { FontWeight = "800", FontSize = "2.25rem", LineHeight = "1.12" },
            H2 = new H2Typography { FontWeight = "760", FontSize = "1.65rem", LineHeight = "1.2" },
            H3 = new H3Typography { FontWeight = "720", FontSize = "1.25rem", LineHeight = "1.25" },
            Button = new ButtonTypography { FontWeight = "700", TextTransform = "none" }
        }
    };
}

