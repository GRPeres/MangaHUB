# Bento Layout

`Components/Bento` contains the base block layout system for MangaHub. Use it when a page or component should be built as dense, responsive, colored blocks instead of normal rows, columns, or cards.

The intended shape is:

```razor
<BentoBlock>
    <BentoItem Width="6" Height="2">
        <BentoPanel>
            ...
        </BentoPanel>
    </BentoItem>

    <BentoItem Width="3" Height="1">
        ...
    </BentoItem>
</BentoBlock>
```

## Components

`BentoBlock`

The opening and closing layout container. It owns the CSS grid, dense packing, responsive breakpoints, row height, spacing, and automatic accent colors.

```razor
<BentoBlock Variant="card">
    ...
</BentoBlock>
```

`BentoItem`

A single block inside a `BentoBlock`. Choose its size with either presets or explicit dimensions.

```razor
<BentoItem Size="BentoBlockSize.Feature">...</BentoItem>
<BentoItem Width="3" Height="1">...</BentoItem>
<BentoItem Width="6" Height="2" Accent="var(--mud-palette-warning)">...</BentoItem>
```

`BentoPanel`

The default colored surface for content that is not already a styled block. It uses the accent color assigned by the parent `BentoBlock`.

```razor
<BentoItem Width="6" Height="2">
    <BentoPanel>
        <MudText Typo="Typo.h3">Featured content</MudText>
    </BentoPanel>
</BentoItem>
```

## Sizing

The grid is based on 12 columns. Think of one visual brick unit as 3 columns on desktop.

| Size | Columns | Rows | Shape |
| --- | ---: | ---: | --- |
| `Small` | 3 | 1 | 1x1 |
| `Wide` | 6 | 1 | 2x1 |
| `Tall` | 3 | 2 | 1x2 |
| `Feature` | 6 | 2 | 2x2 |
| `Hero` | 6 | 2 | 2x2 emphasis |

Use explicit `Width` and `Height` when a preset does not fit.

```razor
<BentoItem Width="9" Height="2">...</BentoItem>
```

## Packing

`BentoBlock` uses CSS Grid with fixed row units and `grid-auto-flow: dense`. This lets smaller items fill gaps around larger items, like stacking bricks.

The row height is controlled by:

```css
--mh-bento-row-height: 5.75rem;
```

The gap is controlled by:

```css
--mh-bento-gap: .7rem;
```

You can override these per block:

```razor
<BentoBlock Class="my-dashboard-bento">
    ...
</BentoBlock>
```

```css
.my-dashboard-bento {
    --mh-bento-row-height: 6.5rem;
    --mh-bento-gap: 1rem;
}
```

## Colors

Each `BentoItem` receives an accent color based on its position in the block. The cycle uses the active MudBlazor theme palette:

- primary
- secondary
- info
- success
- warning
- tertiary

The assigned values are:

```css
--mh-block-accent
--mh-user-accent
```

`BentoPanel` and `MangaInfoBlock` read `--mh-block-accent`. User role blocks read `--mh-user-accent`.

Override a single item with `Accent`:

```razor
<BentoItem Width="3" Height="1" Accent="var(--mud-palette-error)">
    <BentoPanel>Important</BentoPanel>
</BentoItem>
```

## Responsive Behavior

Desktop:

- 12 columns
- fixed row units
- dense packing

Tablet:

- 6 columns
- item width is clamped to available columns

Mobile:

- 2 columns
- item height collapses to one row to avoid giant blocks

Tiny mobile:

- 1 column

## Usage Examples

Basic page section:

```razor
<BentoBlock>
    <BentoItem Size="BentoBlockSize.Feature">
        <BentoPanel>
            <MudText Typo="Typo.h2">Shelf snapshot</MudText>
        </BentoPanel>
    </BentoItem>

    <BentoItem>
        <MangaInfoBlock Label="Reading" Value="12" />
    </BentoItem>

    <BentoItem Size="BentoBlockSize.Wide">
        <MangaInfoBlock Label="Source" Value="MAL" />
    </BentoItem>
</BentoBlock>
```

Card body:

```razor
<BentoBlock Variant="card">
    <BentoItem Size="BentoBlockSize.Feature">
        <BentoPanel>
            <MangaSummaryBlock Title="@Title" Summary="@Summary" />
        </BentoPanel>
    </BentoItem>

    <BentoItem Size="BentoBlockSize.Tall">
        <MangaInfoBlock Label="Status" Value="@Status" />
    </BentoItem>
</BentoBlock>
```

Custom dimensions:

```razor
<BentoBlock>
    <BentoItem Width="9" Height="2">
        <BentoPanel>Large editorial block</BentoPanel>
    </BentoItem>

    <BentoItem Width="3" Height="1">
        <BentoPanel>Small stat</BentoPanel>
    </BentoItem>
</BentoBlock>
```

## Rules

- Prefer `BentoBlock` and `BentoItem` over direct CSS grid classes.
- Put generic colored content in `BentoPanel`.
- Let automatic accents work unless the block has semantic meaning.
- Use explicit `Width` and `Height` only when presets are not enough.
- Keep page-specific CSS for page-specific spacing only, not bento packing.
