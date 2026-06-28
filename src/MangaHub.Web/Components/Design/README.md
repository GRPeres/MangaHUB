# MangaHub Design Components

This folder owns MangaHub's reusable visual language over third-party UI primitives.

For bento layouts, prefer these wrappers instead of using `BzBento` directly:

```razor
<MangaBento>
    <MangaBentoTile ColSpan="2" RowSpan="2" Scheme="deep" CornerIcon="@Icons.Material.Filled.AutoStories">
        ...
    </MangaBentoTile>

    <MangaBentoTile Scheme="primary">
        ...
    </MangaBentoTile>
</MangaBento>
```

`MangaBento` sets the default BlazzyMotion theme, column count, tight spacing, and package override class.

`MangaBentoTile` owns the actual MangaHub surface: radius, padding, readable text colors, corner icon, hover behavior, and palette schemes.

Available schemes:

- `primary`
- `secondary`
- `soft`
- `deep`
- `ink`
- `warm`

Keep page CSS focused on page-specific inner layout. Put shared bento spacing, color, and surface behavior here.
