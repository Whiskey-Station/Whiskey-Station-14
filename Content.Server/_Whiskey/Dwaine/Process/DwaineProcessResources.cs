// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Server._Whiskey.Dwaine.Process;

/// <summary>
/// FIFO text stream that rejects overflow instead of evicting unread process data.
/// </summary>
public sealed class DwaineProcessTextStream
{
    public const int HardMaxChunkLength = 8192;

    private readonly Queue<string> _chunks = new();
    private readonly int _chunkCapacity;
    private readonly int _characterCapacity;

    public int Count => _chunks.Count;
    public int CharacterCount { get; private set; }

    public DwaineProcessTextStream(int chunkCapacity, int characterCapacity)
    {
        if (chunkCapacity <= 0 || characterCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(chunkCapacity));

        _chunkCapacity = chunkCapacity;
        _characterCapacity = characterCapacity;
    }

    public bool TryWrite(string? text)
    {
        if (text is null
            || text.Length > HardMaxChunkLength
            || _chunks.Count >= _chunkCapacity
            || text.Length > _characterCapacity - CharacterCount)
        {
            return false;
        }

        _chunks.Enqueue(text);
        CharacterCount += text.Length;
        return true;
    }

    public bool TryRead(out string text)
    {
        if (!_chunks.TryDequeue(out text!))
        {
            text = string.Empty;
            return false;
        }

        CharacterCount -= text.Length;
        return true;
    }

    public string[] Snapshot()
    {
        return _chunks.ToArray();
    }

    public void Clear()
    {
        _chunks.Clear();
        CharacterCount = 0;
    }
}

public sealed class DwaineProcessEnvironment
{
    public const int HardMaxNameLength = 64;
    public const int HardMaxValueLength = 2048;

    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
    private readonly int _entryCapacity;
    private readonly int _characterCapacity;

    public int Count => _values.Count;
    public int CharacterCount { get; private set; }

    public DwaineProcessEnvironment(int entryCapacity, int characterCapacity)
    {
        if (entryCapacity <= 0 || characterCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(entryCapacity));

        _entryCapacity = entryCapacity;
        _characterCapacity = characterCapacity;
    }

    public bool TrySet(string? name, string? value)
    {
        if (!IsValidName(name) || value is null || value.Length > HardMaxValueLength)
            return false;

        var key = name!;
        var previousCharacters = _values.TryGetValue(key, out var previous)
            ? key.Length + previous.Length
            : 0;
        if (previous is null && _values.Count >= _entryCapacity)
            return false;

        var nextCharacters = CharacterCount - previousCharacters + key.Length + value.Length;
        if (nextCharacters > _characterCapacity)
            return false;

        _values[key] = value;
        CharacterCount = nextCharacters;
        return true;
    }

    public bool TryGet(string name, out string value)
    {
        return _values.TryGetValue(name, out value!);
    }

    public bool TryRemove(string name)
    {
        if (!_values.Remove(name, out var value))
            return false;

        CharacterCount -= name.Length + value.Length;
        return true;
    }

    public IReadOnlyDictionary<string, string> Snapshot()
    {
        return new Dictionary<string, string>(_values, StringComparer.Ordinal);
    }

    public DwaineProcessEnvironment Clone()
    {
        var clone = new DwaineProcessEnvironment(_entryCapacity, _characterCapacity);
        foreach (var (name, value) in _values)
            clone.TrySet(name, value);
        return clone;
    }

    private static bool IsValidName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > HardMaxNameLength || char.IsDigit(name[0]))
            return false;

        foreach (var character in name)
        {
            if (!(character is >= 'A' and <= 'Z'
                  or >= 'a' and <= 'z'
                  or >= '0' and <= '9'
                  or '_'))
            {
                return false;
            }
        }

        return true;
    }
}

public readonly record struct DwaineProcessMessage(
    DwaineProcessId Sender,
    string Type,
    string Payload,
    TimeSpan Timestamp);

public sealed class DwaineProcessMailbox
{
    public const int HardMaxTypeLength = 32;
    public const int HardMaxPayloadLength = 4096;

    private readonly Queue<DwaineProcessMessage> _messages = new();
    private readonly int _messageCapacity;
    private readonly int _characterCapacity;

    public int Count => _messages.Count;
    public int CharacterCount { get; private set; }

    public DwaineProcessMailbox(int messageCapacity, int characterCapacity)
    {
        if (messageCapacity <= 0 || characterCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(messageCapacity));

        _messageCapacity = messageCapacity;
        _characterCapacity = characterCapacity;
    }

    public bool IsValidMessage(string? type, string? payload)
    {
        if (string.IsNullOrWhiteSpace(type)
            || type.Length > HardMaxTypeLength
            || payload is null
            || payload.Length > HardMaxPayloadLength)
        {
            return false;
        }

        foreach (var character in type)
        {
            if (!(character is >= 'a' and <= 'z'
                  or >= '0' and <= '9'
                  or '.' or '-' or '_'))
            {
                return false;
            }
        }

        return payload.IndexOf('\0') < 0;
    }

    public bool TryWrite(DwaineProcessMessage message)
    {
        var characters = message.Type.Length + message.Payload.Length;
        if (!IsValidMessage(message.Type, message.Payload)
            || _messages.Count >= _messageCapacity
            || characters > _characterCapacity - CharacterCount)
        {
            return false;
        }

        _messages.Enqueue(message);
        CharacterCount += characters;
        return true;
    }

    public bool TryRead(out DwaineProcessMessage message)
    {
        if (!_messages.TryDequeue(out message))
            return false;

        CharacterCount -= message.Type.Length + message.Payload.Length;
        return true;
    }

    public void Clear()
    {
        _messages.Clear();
        CharacterCount = 0;
    }
}
