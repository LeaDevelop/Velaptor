// <copyright file="Font.cs" company="KinsonDigital">
// Copyright (c) KinsonDigital. All rights reserved.
// </copyright>

namespace Velaptor.Content.Fonts;

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Text;
using Caching;
using Exceptions;
using ExtensionMethods;
using Graphics;
using NativeInterop.Services;
using Services;
using Velaptor.Services;

/// <summary>
/// Represents a font with a set size and style that can be used to render text to the screen.
/// </summary>
public sealed class Font : IFont
{
    private const char InvalidCharacter = '□';
    private readonly IFreeTypeService freeTypeService;
    private readonly IFontStatsService fontStatsService;
    private readonly IFontAtlasService fontAtlasService;
    private readonly IItemCache<string, ITexture> textureCache;
    private readonly Dictionary<string, SizeF> textSizeCache = new ();
    private readonly nint facePtr;
    private readonly GlyphMetrics invalidGlyph;
    private readonly char[] availableGlyphCharacters =
    [
        'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j', 'k', 'l', 'm', 'n', 'o', 'p', 'q', 'r', 's', 't', 'u', 'v', 'w', 'x', 'y', 'z',
        'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J', 'K', 'L', 'M', 'N', 'O', 'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z',
        '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '`', '!', '@', '#', '$', '%', '^', '&', '*', '(', ')', '-', '=',
        '~', '_', '+', '[', ']', '\\', ';', '\'', ',', '.', '/', '{', '}', '|', ':', '"', '<', '>', '?', ' '
    ];
    private readonly bool fontInitialized;
    private GlyphMetrics[] metrics;
    private FontStats[]? fontStats;
    private FontStyle fontStyle;
    private uint size;

    /// <summary>
    /// Initializes a new instance of the <see cref="Font"/> class.
    /// </summary>
    /// <param name="texture">The font atlas texture that contains bitmap data for all the available glyphs.</param>
    /// <param name="freeTypeService">Provides extensions/helpers to <c>FreeType</c> library functionality.</param>
    /// <param name="fontStatsService">Used to gather stats about content or system fonts.</param>
    /// <param name="fontAtlasService">Creates font atlas textures and glyph metric data.</param>
    /// <param name="textureCache">Creates and caches textures for later retrieval.</param>
    /// <param name="name">The name of the font content.</param>
    /// <param name="fontFilePath">The path to the font content.</param>
    /// <param name="size">The size to set the font.</param>
    /// <param name="isDefaultFont">True if the font is a default font.</param>
    /// <param name="glyphMetrics">The glyph metric data including the atlas location of all glyphs in the atlas.</param>
    internal Font(
        ITexture texture,
        IFreeTypeService freeTypeService,
        IFontStatsService fontStatsService,
        IFontAtlasService fontAtlasService,
        IItemCache<string, ITexture> textureCache,
        string name,
        string fontFilePath,
        uint size,
        bool isDefaultFont,
        GlyphMetrics[] glyphMetrics)
    {
        ArgumentNullException.ThrowIfNull(texture);
        ArgumentNullException.ThrowIfNull(freeTypeService);
        ArgumentNullException.ThrowIfNull(fontStatsService);
        ArgumentNullException.ThrowIfNull(fontAtlasService);
        ArgumentNullException.ThrowIfNull(textureCache);
        ArgumentException.ThrowIfNullOrEmpty(name);

        Atlas = texture;
        this.freeTypeService = freeTypeService;
        this.fontStatsService = fontStatsService;
        this.fontAtlasService = fontAtlasService;
        this.textureCache = textureCache;

        this.metrics = glyphMetrics;
        this.invalidGlyph = Array.Find(glyphMetrics, m => m.Glyph == InvalidCharacter);

        this.facePtr = this.freeTypeService.CreateFontFace(fontFilePath);

        this.size = size;
        Name = name;
        FilePath = fontFilePath;
        FamilyName = this.freeTypeService.GetFamilyName(this.facePtr, fontFilePath);
        Style = this.freeTypeService.GetFontStyle(this.facePtr, fontFilePath);
        IsDefaultFont = isDefaultFont;

        GetFontStatData();

        HasKerning = this.freeTypeService.HasKerning(this.facePtr);
        LineSpacing = this.freeTypeService.GetFontScaledLineSpacing(this.facePtr, Size);

        this.fontInitialized = true;
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public FontSource Source => this.fontStats?.Length <= 0
        ? FontSource.Unknown
        : (from s in this.fontStats where s.Style == this.fontStyle select s.Source).FirstOrDefault();

    /// <inheritdoc/>
    public string FilePath { get; }

    /// <inheritdoc/>
    public ITexture Atlas { get; private set; }

    /// <inheritdoc/>
    /// <remarks>The size of the font has a max size of 100.</remarks>
    public uint Size
    {
        get => this.size;
        set
        {
            this.size = value > 100 ? 100 : value;

            if (this.fontInitialized && this.size > 0u)
            {
                RebuildAtlasTexture();
            }

            // Clear the entire size cache since the size has changed
            this.textSizeCache.Clear();
        }
    }

    /// <inheritdoc/>
    public FontStyle Style
    {
        get => this.fontStyle;
        set
        {
            this.fontStyle = value;

            if (this.fontInitialized)
            {
                RebuildAtlasTexture();
            }
        }
    }

    /// <inheritdoc/>
    public bool IsDefaultFont { get; }

    /// <inheritdoc/>
    public IEnumerable<FontStyle> AvailableStylesForFamily
        => this.fontStats is null
            ? Array.Empty<FontStyle>().AsReadOnly()
            : this.fontStats.Select(s => s.Style).ToArray().AsReadOnly();

    /// <inheritdoc/>
    public string FamilyName { get; }

    /// <inheritdoc/>
    public bool HasKerning { get; }

    /// <inheritdoc/>
    public float LineSpacing { get; private set; }

    /// <inheritdoc/>
    public bool CacheEnabled { get; set; } = true;

    /// <inheritdoc/>
    public int MaxCacheSize { get; set; } = 1000;

    /// <inheritdoc/>
    public IReadOnlyCollection<GlyphMetrics> Metrics => this.metrics.AsReadOnly();

    /// <inheritdoc/>
    public SizeF Measure(string text)
    {
        // Trim all the '\n' and '\r' characters from the end
        text = text.TrimNewLineFromEnd();

        // Just in case the text was ONLY '\r' and/or '\n' characters, nothing would be left.
        if (string.IsNullOrEmpty(text))
        {
            return SizeF.Empty;
        }

        // Normalize the line endings
        if (text.Contains("\r\n"))
        {
            text = text.Replace("\r\n", "\n");
        }

        if (CacheEnabled && this.textSizeCache.TryGetValue(text, out SizeF measure))
        {
            return measure;
        }

        var lines = text.Split("\n");

        var (largestWidth, totalHeight) = CalcTextDimensions(lines);

        var textSize = new SizeF(largestWidth, totalHeight);

        AddToCache(text, textSize);

        return textSize;
    }

    /// <summary>
    /// Returns all the glyph metrics for the given text.
    /// </summary>
    /// <param name="text">The text to convert to glyph metrics.</param>
    /// <returns>The list of glyph metrics of the given <paramref name="text"/>.</returns>
    public GlyphMetrics[] ToGlyphMetrics(string text)
    {
        var result = new List<GlyphMetrics>();

        foreach (var character in text)
        {
            // If the character is a valid glyph
            if (this.availableGlyphCharacters.Contains(character))
            {
                var glyphIndex = this.metrics.IndexOf(metric => metric.Glyph == character);
                result.Add(this.metrics[glyphIndex]);
            }
            else
            {
                result.Add(this.invalidGlyph);
            }
        }

        return result.ToArray();
    }

    /// <summary>
    /// Gets the kerning value between two glyphs.
    /// </summary>
    /// <param name="leftGlyphIndex">The character index of the glyph to the left of the right glyph.</param>
    /// <param name="rightGlyphIndex">The character index of the glyph to the right of the left glyph.</param>
    /// <returns>The kerning (horizontal spacing) between the glyphs.</returns>
    /// <remarks>
    /// Refer to the URL below for more info.
    /// <para>https://freetype.org/freetype2/docs/glyphs/glyphs-4.html#section-1.</para>
    /// </remarks>
    public float GetKerning(uint leftGlyphIndex, uint rightGlyphIndex)
        => this.freeTypeService.GetKerning(this.facePtr, leftGlyphIndex, rightGlyphIndex);

    /// <inheritdoc/>
    /// <remarks>
    ///     The bounds include the width, height, and position of the character relative to
    ///     the <paramref name="textPos"/>.  The position is relative to the top left corner of the character.
    /// </remarks>
    public IEnumerable<(char character, RectangleF bounds)> GetCharacterBounds(string text, Vector2 textPos)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Array.Empty<(char, RectangleF)>();
        }

        var textMetrics = ToGlyphMetrics(text);

        var result = new List<(char character, RectangleF bounds)>();

        var leftGlyphIndex = 0u;

        foreach (var currentCharMetric in textMetrics)
        {
            textPos.X += GetKerning(leftGlyphIndex, currentCharMetric.CharIndex);

            // Calculate the height offset
            var heightOffset = currentCharMetric.GlyphHeight - currentCharMetric.HoriBearingY;

            // Adjust for characters that have a negative horizontal bearing Y
            // For example, the '_' character
            if (currentCharMetric.HoriBearingY < 0)
            {
                heightOffset += currentCharMetric.HoriBearingY;
            }

            // Get the width of the glyph
            var boundsWidth = currentCharMetric.Glyph == ' '
                ? currentCharMetric.HorizontalAdvance
                : currentCharMetric.GlyphBounds.Width;

            // Create the destination rect
            RectangleF charBounds = default;
            charBounds.X = textPos.X;
            charBounds.Y = textPos.Y + heightOffset;
            charBounds.Width = boundsWidth;
            charBounds.Height = currentCharMetric.GlyphBounds.Height <= 0 ? 1 : currentCharMetric.GlyphBounds.Height;

            result.Add((currentCharMetric.Glyph, charBounds));

            // Horizontally advance to the next glyph
            textPos.X += currentCharMetric.HorizontalAdvance;

            leftGlyphIndex = currentCharMetric.CharIndex;
        }

        return result.ToArray();
    }

    /// <inheritdoc/>
    /// <remarks>
    ///     The bounds include the width, height, and position of the character relative to
    ///     the <paramref name="textPos"/>.  The position is relative to the top left corner of the character.
    /// </remarks>
    public IEnumerable<(char character, RectangleF bounds)> GetCharacterBounds(StringBuilder text, Vector2 textPos) => GetCharacterBounds(text.ToString(), textPos);

    /// <summary>
    /// Adds the given text and size to the cache.
    /// </summary>
    /// <param name="text">The text to add.</param>
    /// <param name="textSize">The size of the text to add.</param>
    private void AddToCache(string text, SizeF textSize)
    {
        if (!CacheEnabled || !this.textSizeCache.TryAdd(text, textSize))
        {
            return;
        }

        if (this.textSizeCache.Count <= MaxCacheSize)
        {
            return;
        }

        var removeKey = this.textSizeCache.Keys.First();
        this.textSizeCache.Remove(removeKey);
    }

    /// <summary>
    /// Gets all the stats for the font.
    /// </summary>
    private void GetFontStatData()
    {
        // First collect all the data from the content directory
        this.fontStats = this.fontStatsService.GetContentStatsForFontFamily(FamilyName);

        bool AllStylesFound()
        {
            const FontStyle boldItalic = FontStyle.Bold | FontStyle.Italic;

            return this.fontStats.Length == 4 && Array.TrueForAll(
                this.fontStats,
                d => d.Style is FontStyle.Regular or FontStyle.Bold or FontStyle.Italic or boldItalic);
        }

        // If all four styles have been found and finished
        if (AllStylesFound())
        {
            return;
        }

        var newList = new List<FontStats>();
        newList.AddRange(this.fontStats);
        this.fontStats = newList.ToArray();
    }

    /// <summary>
    /// Rebuilds the font atlas texture and glyph metrics.
    /// </summary>
    /// <exception cref="FontException">Thrown if the current style that is being attempted does not exist.</exception>
    private void RebuildAtlasTexture()
    {
        var fontFilePath = string.Empty;

        foreach (var fontStat in this.fontStats ?? [])
        {
            if (fontStat.Style == this.fontStyle)
            {
                fontFilePath = fontStat.FontFilePath;
            }
        }

        if (string.IsNullOrEmpty(fontFilePath))
        {
            throw new FontException($"The font style '{this.fontStyle}' does not exist for the font family '{FamilyName}'.");
        }

        var filePathWithMetaData = $"{fontFilePath}|size:{Size}";
        Atlas = this.textureCache.GetItem(filePathWithMetaData);

        (_, GlyphMetrics[] glyphMetrics) = this.fontAtlasService.CreateAtlas(fontFilePath, Size);

        LineSpacing = this.freeTypeService.GetFontScaledLineSpacing(this.facePtr, Size);

        this.metrics = glyphMetrics;
    }

    /// <summary>
    /// Returns the size of the given single <paramref name="line"/> of text.
    /// </summary>
    /// <param name="line">The line of text to measure.</param>
    /// <returns>The size of the <paramref name="line"/> of text.</returns>
    private SizeF MeasureLine(string line)
    {
        var width = 0f;
        var height = 0f;
        var leftCharacterIndex = 0u;

        foreach (var character in line)
        {
            var charMetric = this.availableGlyphCharacters.Contains(character)
                ? this.metrics[this.metrics.IndexOf(metric => metric.Glyph == character)]
                : this.invalidGlyph;

            width += HasKerning
                ? this.freeTypeService.GetKerning(this.facePtr, leftCharacterIndex, charMetric.CharIndex)
                : 0;

            width += charMetric.HorizontalAdvance;

            height = charMetric.GlyphHeight > height
                ? charMetric.GlyphHeight
                : height;

            leftCharacterIndex = charMetric.CharIndex;
        }

        return new SizeF(width, height);
    }

    /// <summary>
    /// Calculates the dimensions of the given <paramref name="lines"/> of text.
    /// </summary>
    /// <param name="lines">The lines to use in the measurement.</param>
    /// <returns>The dimensions of all the lines.</returns>
    private (float maxWidth, float totalHeight) CalcTextDimensions(IReadOnlyList<string> lines)
    {
        var maxWidth = 0f;
        var totalHeight = 0f;

        for (var i = 0; i < lines.Count; i++)
        {
            var lineSize = MeasureLine(lines[i]);

            var isLastLine = i == lines.Count - 1;
            totalHeight += lineSize.Height;
            totalHeight += isLastLine ? 0 : LineSpacing - lineSize.Height;

            if (lineSize.Width > maxWidth)
            {
                maxWidth = lineSize.Width;
            }
        }

        return (maxWidth, totalHeight);
    }
}
