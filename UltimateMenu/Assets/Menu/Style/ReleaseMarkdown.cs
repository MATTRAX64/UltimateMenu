using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace UltimateMenu
{
    public partial class MenuUI
    {
        static readonly Uri ReleaseRepository = new Uri("https://github.com/MATTRAX64/UltimateMenu/");
        static readonly Regex MarkdownInline = new Regex(
            @"(?<link>\[(?<caption>[^\]\r\n]+)\]\((?<url>[^\s)]+)\))|(?<auto>https?://[^\s<>]+|(?<![\w/])(?i:issues|pull)/\d+\b)|(?<bold>\*\*[^*]+\*\*|__[^_]+__)|(?<italic>\*[^*]+\*|(?<!\w)_[^_]+_(?!\w))|(?<code>`[^`]+`)");
        static readonly Regex MarkdownWords = new Regex(@"\s+|\S+");
        static readonly Regex MarkdownBlock = new Regex(@"^(?:(?<heading>#{1,6})\s+|(?<bullet>[-+*])\s+|(?<quote>>+)\s*|(?<number>\d+[.)])\s+)");

        static string? MarkdownUrl(string target)
        {
            if (!Uri.TryCreate(ReleaseRepository, target, out var uri)) return null;
            return uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp ? uri.AbsoluteUri : null;
        }

        float _releaseMarkdownWidth;

        void DrawReleaseMarkdown(string notes, string section)
        {
            float width = _expandedWidths.TryGetValue(section, out float expanded) ? expanded : _rect.width - 100f * UI.Scale;
            // Keep a small reserve for the nested area's layout margins.
            if (Event.current.type == EventType.Layout || _releaseMarkdownWidth <= 0f)
                _releaseMarkdownWidth = Mathf.Max(24f, width - 16f * UI.Scale);
            width = _releaseMarkdownWidth;
            bool fenced = false;
            foreach (string raw in notes.Split('\n'))
            {
                string line = raw.TrimEnd();
                if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
                {
                    fenced = !fenced;
                    continue;
                }
                if (string.IsNullOrWhiteSpace(line))
                {
                    GUILayout.Space(5f * UI.Scale);
                    continue;
                }

                Match block = MarkdownBlock.Match(line);
                bool heading = !fenced && block.Groups["heading"].Success;
                bool quote = !fenced && block.Groups["quote"].Success;
                var row = new List<(string Text, GUIStyle Style, string? Url, float Width)>();
                float used = 0f;
                float rowHeight = Mathf.Max(UI.Px(heading ? 13 : 10) + 5f * UI.Scale, 18f * UI.Scale);

                void Flush()
                {
                    if (row.Count == 0) return;
                    Rect bounds = GUILayoutUtility.GetRect(0f, rowHeight, GUILayout.ExpandWidth(true));
                    float x = bounds.x;
                    foreach (var part in row)
                    {
                        var cell = new Rect(x, bounds.y, Mathf.Min(part.Width, Mathf.Max(0f, bounds.xMax - x)), bounds.height);
                        DrawSingleLineLabel(cell, part.Text, part.Style);
                        if (part.Url != null && ControlButton(cell, new GUIContent("", part.Url), GUIStyle.none))
                            Application.OpenURL(part.Url);
                        x += part.Width;
                    }
                    row.Clear();
                    used = 0f;
                }

                void Add(string text, bool bold = false, bool italic = false, bool code = false, string? url = null)
                {
                    var style = UI.Sty(url != null ? Theme.Accent : quote || code ? Theme.Dim : Theme.Text,
                        heading ? 13 : 10, bold || heading, TextAnchor.MiddleLeft);
                    style.padding = new RectOffset();
                    style.richText = false;
                    style.wordWrap = false;
                    if (italic) style.fontStyle = FontStyle.Italic;
                    float sentinel = style.CalcSize(new GUIContent("|")).x;
                    foreach (Match word in MarkdownWords.Matches(text))
                    {
                        string token = word.Value;
                        bool space = char.IsWhiteSpace(token[0]);
                        if (space && used == 0f) continue;
                        float advance = style.CalcSize(new GUIContent(token + "|")).x - sentinel;
                        foreach (Match emoji in MenuEmoji.Sequences.Matches(token))
                        {
                            if (MenuEmoji.Get(emoji.Value) != null)
                                advance += Mathf.Ceil(style.lineHeight * 1.08f) -
                                    (style.CalcSize(new GUIContent(emoji.Value + "|")).x - sentinel);
                        }
                        advance = Mathf.Max(1f, Mathf.Ceil(advance));
                        if (used + advance > width)
                        {
                            Flush();
                            if (space) continue;
                        }
                        row.Add((token, style, space ? null : url, advance));
                        used += advance;
                    }
                }

                if (!fenced && block.Success)
                {
                    if (block.Groups["bullet"].Success) Add("• ");
                    if (block.Groups["number"].Success) Add(block.Groups["number"].Value + " ");
                    if (quote) Add("› ");
                    line = line.Substring(block.Length);
                }

                void ParseInline(string text, bool bold = false, bool italic = false)
                {
                    int cursor = 0;
                    foreach (Match token in MarkdownInline.Matches(text))
                    {
                        Add(text.Substring(cursor, token.Index - cursor), bold, italic);
                        if (token.Groups["link"].Success)
                            Add(token.Groups["caption"].Value, bold, italic, url: MarkdownUrl(token.Groups["url"].Value));
                        else if (token.Groups["auto"].Success)
                        {
                            string link = token.Value.TrimEnd('.', ',', ';', ')');
                            Add(link, bold, italic, url: MarkdownUrl(link));
                            Add(token.Value.Substring(link.Length), bold, italic);
                        }
                        else if (token.Groups["code"].Success)
                            Add(token.Value.Substring(1, token.Length - 2), bold, italic, code: true);
                        else
                        {
                            bool strong = token.Groups["bold"].Success;
                            int marker = strong ? 2 : 1;
                            ParseInline(token.Value.Substring(marker, token.Length - 2 * marker),
                                bold || strong, italic || !strong);
                        }
                        cursor = token.Index + token.Length;
                    }
                    Add(text.Substring(cursor), bold, italic);
                }

                if (fenced) Add(line, code: true);
                else ParseInline(line);
                Flush();
            }
        }
    }
}
