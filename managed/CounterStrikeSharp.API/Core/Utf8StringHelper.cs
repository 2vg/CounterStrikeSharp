/*
 * Copyright (c) 2024 CounterStrikeSharp Contributors
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 */

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Utf8StringInterpolation;

namespace CounterStrikeSharp.API.Core
{
  /// <summary>
  /// Utf8StringInterpolationを活用した高性能文字列処理ヘルパー
  /// </summary>
  public static class Utf8StringHelper
  {
    private static readonly ArrayPool<byte> _bufferPool = ArrayPool<byte>.Shared;

    /// <summary>
    /// UTF8文字列補間を使用してネイティブポインタを直接作成
    /// 従来の文字列補間よりも高性能
    /// </summary>
    public static unsafe IntPtr FormatToNative<T>(ref T format, ConcurrentQueue<Action> finalizers)
        where T : IUtf8SpanFormattable
    {
      // Utf8StringInterpolationを使用してUTF8バイト配列を直接生成
      var utf8Bytes = Utf8String.Format(ref format);

      if (utf8Bytes.Length == 0)
      {
        var emptyPtr = Marshal.AllocHGlobal(1);
        Marshal.WriteByte(emptyPtr, 0);
        finalizers?.Enqueue(() => Marshal.FreeHGlobal(emptyPtr));
        return emptyPtr;
      }

      // null終端を含むメモリを割り当て
      var nativePtr = Marshal.AllocHGlobal(utf8Bytes.Length + 1);

      // UTF8バイトを直接コピー
      Marshal.Copy(utf8Bytes, 0, nativePtr, utf8Bytes.Length);
      Marshal.WriteByte(nativePtr, utf8Bytes.Length, 0); // null終端

      finalizers?.Enqueue(() => Marshal.FreeHGlobal(nativePtr));
      return nativePtr;
    }

    /// <summary>
    /// IBufferWriter&lt;byte&gt;を使用した高性能文字列フォーマット
    /// </summary>
    public static unsafe IntPtr FormatToNativeWithBuffer<T>(ref T format, ConcurrentQueue<Action> finalizers)
        where T : IUtf8SpanFormattable
    {
      using var buffer = Utf8String.CreateWriter(out var writer);

      // Utf8StringWriterに直接書き込み
      writer.AppendFormat(ref format);
      writer.Flush();

      var writtenSpan = buffer.WrittenSpan;
      if (writtenSpan.Length == 0)
      {
        var emptyPtr = Marshal.AllocHGlobal(1);
        Marshal.WriteByte(emptyPtr, 0);
        finalizers?.Enqueue(() => Marshal.FreeHGlobal(emptyPtr));
        return emptyPtr;
      }

      // null終端を含むメモリを割り当て
      var nativePtr = Marshal.AllocHGlobal(writtenSpan.Length + 1);

      // UTF8バイトを直接コピー
      fixed (byte* srcPtr = writtenSpan)
      {
        Buffer.MemoryCopy(srcPtr, (void*)nativePtr, writtenSpan.Length, writtenSpan.Length);
      }
      Marshal.WriteByte(nativePtr, writtenSpan.Length, 0); // null終端

      finalizers?.Enqueue(() => Marshal.FreeHGlobal(nativePtr));
      return nativePtr;
    }

    /// <summary>
    /// 複数の文字列を効率的に結合してネイティブポインタを作成
    /// </summary>
    public static unsafe IntPtr JoinToNative(ReadOnlySpan<string> values, string separator, ConcurrentQueue<Action> finalizers)
    {
      if (values.Length == 0)
      {
        var emptyPtr = Marshal.AllocHGlobal(1);
        Marshal.WriteByte(emptyPtr, 0);
        finalizers?.Enqueue(() => Marshal.FreeHGlobal(emptyPtr));
        return emptyPtr;
      }

      // Utf8String.Joinを使用して効率的に結合
      var utf8Bytes = Utf8String.Join(separator, values.ToArray());

      // null終端を含むメモリを割り当て
      var nativePtr = Marshal.AllocHGlobal(utf8Bytes.Length + 1);

      // UTF8バイトを直接コピー
      Marshal.Copy(utf8Bytes, 0, nativePtr, utf8Bytes.Length);
      Marshal.WriteByte(nativePtr, utf8Bytes.Length, 0); // null終端

      finalizers?.Enqueue(() => Marshal.FreeHGlobal(nativePtr));
      return nativePtr;
    }

    /// <summary>
    /// 文字列配列を効率的に連結してネイティブポインタを作成
    /// </summary>
    public static unsafe IntPtr ConcatToNative(ReadOnlySpan<string> values, ConcurrentQueue<Action> finalizers)
    {
      if (values.Length == 0)
      {
        var emptyPtr = Marshal.AllocHGlobal(1);
        Marshal.WriteByte(emptyPtr, 0);
        finalizers?.Enqueue(() => Marshal.FreeHGlobal(emptyPtr));
        return emptyPtr;
      }

      // Utf8String.Concatを使用して効率的に連結
      var utf8Bytes = Utf8String.Concat(values.ToArray());

      // null終端を含むメモリを割り当て
      var nativePtr = Marshal.AllocHGlobal(utf8Bytes.Length + 1);

      // UTF8バイトを直接コピー
      Marshal.Copy(utf8Bytes, 0, nativePtr, utf8Bytes.Length);
      Marshal.WriteByte(nativePtr, utf8Bytes.Length, 0); // null終端

      finalizers?.Enqueue(() => Marshal.FreeHGlobal(nativePtr));
      return nativePtr;
    }
  }

  /// <summary>
  /// ScriptContextの拡張メソッド
  /// Utf8StringInterpolationを活用した高性能な文字列処理
  /// </summary>
  public static class ScriptContextExtensions
  {
    /// <summary>
    /// UTF8文字列補間を使用した高性能なPushString
    /// 使用例: context.PushUtf8String($"Player {playerName} has {score} points");
    /// </summary>
    public static unsafe void PushUtf8String(this ScriptContext context, ref Utf8StringWriter<ArrayBufferWriter<byte>> format)
    {
      fixed (fxScriptContext* cxt = &context.m_extContext)
      {
        // Utf8StringWriterから直接UTF8バイトを取得
        format.Flush();
        var buffer = format.GetUnderlyingBuffer();
        var writtenSpan = buffer.WrittenSpan;

        IntPtr nativePtr;
        if (writtenSpan.Length == 0)
        {
          nativePtr = Marshal.AllocHGlobal(1);
          Marshal.WriteByte(nativePtr, 0);
        }
        else
        {
          // null終端を含むメモリを割り当て
          nativePtr = Marshal.AllocHGlobal(writtenSpan.Length + 1);

          // UTF8バイトを直接コピー
          fixed (byte* srcPtr = writtenSpan)
          {
            Buffer.MemoryCopy(srcPtr, (void*)nativePtr, writtenSpan.Length, writtenSpan.Length);
          }
          Marshal.WriteByte(nativePtr, writtenSpan.Length, 0); // null終端
        }

        context.ms_finalizers.Enqueue(() => Marshal.FreeHGlobal(nativePtr));
        *(IntPtr*)(&cxt->functionData[8 * cxt->numArguments]) = nativePtr;
        cxt->numArguments++;
      }
    }
  }
}