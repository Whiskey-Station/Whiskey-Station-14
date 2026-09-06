// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Server._Whiskey.Dwaine.Hardware;

/// <summary>
/// A deterministic FIFO text buffer bounded by both entries and characters.
/// </summary>
public sealed class DwaineBoundedTextBuffer
{
    private readonly Queue<string> _lines = new();
    private readonly int _lineLimit;
    private readonly int _characterLimit;
    private int _characters;

    public int Count => _lines.Count;
    public int CharacterCount => _characters;

    public DwaineBoundedTextBuffer(int lineLimit, int characterLimit)
    {
        if (lineLimit <= 0)
            throw new ArgumentOutOfRangeException(nameof(lineLimit));

        if (characterLimit <= 0)
            throw new ArgumentOutOfRangeException(nameof(characterLimit));

        _lineLimit = lineLimit;
        _characterLimit = characterLimit;
    }

    public void Add(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length > _characterLimit)
            text = text[.._characterLimit];

        _lines.Enqueue(text);
        _characters += text.Length;

        while (_lines.Count > _lineLimit || _characters > _characterLimit)
        {
            var removed = _lines.Dequeue();
            _characters -= removed.Length;
        }
    }

    public string[] Snapshot()
    {
        return _lines.ToArray();
    }

    public void Clear()
    {
        _lines.Clear();
        _characters = 0;
    }
}
