using System;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using CounterStrikeSharp.API.Core;
using Cysharp.Text;

namespace CounterStrikeSharp.API.Modules.Memory;

public class Schema
{
    private static readonly ConcurrentDictionary<(string className, string propertyName), short> _schemaOffsets = new();

    private static readonly FrozenSet<string> _cs2BadList = new[]
    {
        "m_bIsValveDS",
        "m_bIsQuestEligible",
        // "m_iItemDefinitionIndex", // as of 2023.11.11 this is currently not blocked
        "m_iEntityLevel",
        "m_iItemIDHigh",
        "m_iItemIDLow",
        "m_iAccountID",
        "m_iEntityQuality",

        "m_bInitialized",
        "m_szCustomName",
        "m_iAttributeDefinitionIndex",
        "m_iRawValue32",
        "m_iRawInitialValue32",
        "m_flValue", // MNetworkAlias "m_iRawValue32"
        "m_flInitialValue", // MNetworkAlias "m_iRawInitialValue32"
        "m_bSetBonus",
        "m_nRefundableCurrency",

        "m_OriginalOwnerXuidLow",
        "m_OriginalOwnerXuidHigh",

        "m_nFallbackPaintKit",
        "m_nFallbackSeed",
        "m_flFallbackWear",
        "m_nFallbackStatTrak",

        "m_iCompetitiveWins",
        "m_iCompetitiveRanking",
        "m_iCompetitiveRankType",
        "m_iCompetitiveRankingPredicted_Win",
        "m_iCompetitiveRankingPredicted_Loss",
        "m_iCompetitiveRankingPredicted_Tie",

        "m_nActiveCoinRank",
        "m_nMusicID",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static class DataTypeCache<T>
    {
        internal static readonly int Value = (int)(typeof(T).ToDataType() ?? 0);
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowBadField(string className, string propertyName)
        => throw new Exception(ZString.Format("Cannot set or get '{0}::{1}' with \"FollowCS2ServerGuidelines\" option enabled.", className, propertyName));

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static T ThrowNullPointer<T>()
        => throw new ArgumentNullException("pointer", "Schema target points to null.");

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowTooLong(int maxLength)
        => throw new ArgumentException(ZString.Format("String length exceeds maximum length of {0}", maxLength - 1));

    [DoesNotReturn]
    private static void ThrowNullPointer()
        => throw new ArgumentNullException("pointer", "...");

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetClassSize(string className) => NativeAPI.GetSchemaClassSize(className);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static short GetSchemaOffset(string className, string propertyName)
    {
        if (CoreConfig.FollowCS2ServerGuidelines && _cs2BadList.Contains(propertyName))
            ThrowBadField(className, propertyName);

        return _schemaOffsets.GetOrAdd((className, propertyName),
            static key => NativeAPI.GetSchemaOffset(key.className, key.propertyName));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsSchemaFieldNetworked(string className, string propertyName)
    {
        return NativeAPI.IsSchemaFieldNetworked(className, propertyName);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T GetSchemaValue<T>(nint handle, string className, string propertyName)
    {
        if (handle == 0) return ThrowNullPointer<T>();

        return NativeAPI.GetSchemaValueByName<T>(handle, DataTypeCache<T>.Value, className, propertyName);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetSchemaValue<T>(nint handle, string className, string propertyName, T value)
    {
        if (handle == 0) ThrowNullPointer<object>();

        if (CoreConfig.FollowCS2ServerGuidelines && _cs2BadList.Contains(propertyName))
            ThrowBadField(className, propertyName);

        NativeAPI.SetSchemaValueByName<T>(handle, DataTypeCache<T>.Value, className, propertyName, value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T GetDeclaredClass<T>(nint pointer, string className, string memberName)
    {
        if (pointer == 0) return ThrowNullPointer<T>();

        var targetPtr = pointer + GetSchemaOffset(className, memberName);
        object? instance = Activator.CreateInstance(typeof(T), targetPtr);

        if (DisposableMemory.IsDisposableType(typeof(T)))
        {
            DisposableMemory.MarkAsPure(instance);
        }

        return (T)instance;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe ref T GetRef<T>(nint pointer, string className, string memberName)
    {
        if (pointer == 0) ThrowNullPointer<T>();

        return ref Unsafe.AsRef<T>((void*)(pointer + GetSchemaOffset(className, memberName)));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe T GetPointer<T>(nint pointer)
    {
        var pointerTo = Unsafe.Read<nint>((void*)pointer);
        if (pointerTo == 0)
        {
            return default;
        }

        return (T)Activator.CreateInstance(typeof(T), pointerTo);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe T GetPointer<T>(nint pointer, string className, string memberName)
    {
        if (pointer == 0) return ThrowNullPointer<T>();

        var targetPtr = pointer + GetSchemaOffset(className, memberName);
        var pointerTo = Unsafe.Read<nint>((void*)targetPtr);
        if (pointerTo == 0)
        {
            return default;
        }

        return (T)Activator.CreateInstance(typeof(T), pointerTo);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe Span<T> GetFixedArray<T>(nint pointer, string className, string memberName, int count)
    {
        if (pointer == 0) { ThrowNullPointer(); return default; }

        var targetPtr = pointer + GetSchemaOffset(className, memberName);
        return new Span<T>((void*)targetPtr, count);

        // TODO: once we get a correct implementation for this method check for `DisposableMemory` instances and mark them as pure
    }

    /// <summary>
    /// Reads a string from the specified pointer, class name, and member name.
    /// These are for non-networked strings, which are just stored as raw char bytes on the server.
    /// </summary>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string GetString(nint pointer, string className, string memberName)
    {
        return GetSchemaValue<string>(pointer, className, memberName);
    }

    /// <summary>
    /// Reads a UTF8 encoded string from the specified pointer, class name, and member name.
    /// These are for networked strings, which need to be read differently.
    /// </summary>
    /// <param name="pointer"></param>
    /// <param name="className"></param>
    /// <param name="memberName"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string GetUtf8String(nint pointer, string className, string memberName)
    {
        return Utilities.ReadStringUtf8(pointer + GetSchemaOffset(className, memberName));
    }

    // Used to write to `string_t` and `char*` pointer type strings
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void SetString(nint pointer, string className, string memberName, string value)
    {
        SetSchemaValue(pointer, className, memberName, value);
    }

    // Used to write to the char[] specified at the schema location, i.e. char m_iszPlayerName[128];
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal unsafe static void SetStringBytes(nint pointer, string className, string memberName, string value, int maxLength)
    {
        var handle = GetSchemaValue<nint>(pointer, className, memberName);

        if (string.IsNullOrEmpty(value))
        {
            Unsafe.Write((void*)handle, (byte)0);
            return;
        }

        int byteLen = Encoding.UTF8.GetByteCount(value);
        if (byteLen >= maxLength) ThrowTooLong(maxLength);

        Span<byte> tmp = byteLen <= 256 ? stackalloc byte[byteLen] : new byte[byteLen];
        Encoding.UTF8.GetBytes(value, tmp);

        tmp.CopyTo(new Span<byte>((void*)handle, maxLength));
        Unsafe.Write((void*)(handle + byteLen), (byte)0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T GetCustomMarshalledType<T>(nint pointer, string className, string memberName)
    {
        if (pointer == 0) return ThrowNullPointer<T>();

        var targetPtr = pointer + GetSchemaOffset(className, memberName);
        var type = typeof(T);
        object result = type switch
        {
            _ when type == typeof(Color) => Marshaling.ColorMarshaler.NativeToManaged(targetPtr),
            _ => throw new NotSupportedException(),
        };

        return (T)result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetCustomMarshalledType<T>(nint pointer, string className, string memberName, T value)
    {
        if (pointer == 0) ThrowNullPointer<object>();

        var targetPtr = pointer + GetSchemaOffset(className, memberName);
        var type = typeof(T);
        switch (type)
        {
            case var _ when value is Color c:
                Marshaling.ColorMarshaler.ManagedToNative(targetPtr, c);
                break;
            default:
                throw new NotSupportedException();
        }
    }
}
