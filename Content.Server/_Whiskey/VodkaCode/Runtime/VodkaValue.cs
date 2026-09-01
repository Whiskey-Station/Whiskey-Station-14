// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Globalization;
using System.Text;

namespace Content.Server._Whiskey.VodkaCode.Runtime;

internal enum VodkaValueKind : byte
{
    Null,
    Integer,
    Boolean,
    String,
    Handle,
}

internal readonly record struct VodkaValue
{
    public VodkaValueKind Kind { get; }
    public long Integer { get; }
    public bool Boolean { get; }
    public string Text { get; }
    public ulong Handle { get; }
    public int DataBytes { get; }
    public int RuneCount { get; }
    public bool IsWellFormedString { get; }

    private VodkaValue(
        VodkaValueKind kind,
        long integer,
        bool boolean,
        string text,
        ulong handle,
        int dataBytes,
        int runeCount,
        bool isWellFormedString)
    {
        Kind = kind;
        Integer = integer;
        Boolean = boolean;
        Text = text;
        Handle = handle;
        DataBytes = dataBytes;
        RuneCount = runeCount;
        IsWellFormedString = isWellFormedString;
    }

    public static VodkaValue Null => new(VodkaValueKind.Null, 0, false, string.Empty, 0, 1, 0, true);
    public static VodkaValue FromInteger(long value) => new(VodkaValueKind.Integer, value, false, string.Empty, 0, 8, 0, true);
    public static VodkaValue FromBoolean(bool value) => new(VodkaValueKind.Boolean, 0, value, string.Empty, 0, 8, 0, true);
    public static VodkaValue FromHandle(ulong value) => new(VodkaValueKind.Handle, 0, false, string.Empty, value, 8, 0, true);

    public static VodkaValue FromString(string value)
    {
        var runeCount = 0;
        var wellFormed = true;
        for (var index = 0; index < value.Length; index++)
        {
            if (!char.IsSurrogate(value[index]))
            {
                runeCount++;
                continue;
            }

            if (!char.IsHighSurrogate(value[index])
                || index + 1 >= value.Length
                || !char.IsLowSurrogate(value[index + 1]))
            {
                wellFormed = false;
                continue;
            }

            runeCount++;
            index++;
        }

        return new VodkaValue(
            VodkaValueKind.String,
            0,
            false,
            value,
            0,
            Encoding.UTF8.GetByteCount(value),
            runeCount,
            wellFormed);
    }

    public string ToDisplayString()
    {
        return Kind switch
        {
            VodkaValueKind.Null => "null",
            VodkaValueKind.Integer => Integer.ToString(CultureInfo.InvariantCulture),
            VodkaValueKind.Boolean => Boolean ? "true" : "false",
            VodkaValueKind.String => Text,
            VodkaValueKind.Handle => $"handle:{Handle.ToString(CultureInfo.InvariantCulture)}",
            _ => "null",
        };
    }
}
