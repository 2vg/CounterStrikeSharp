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
using System.Text;

namespace CounterStrikeSharp.API.Core
{
  /// <summary>
  /// 高性能な文字列マーシャリングを提供するクラス
  /// String Pooling、stackalloc、ArrayPoolを組み合わせて最適化
  /// </summary>
  public static class StringMarshaler
  {
    private static readonly ArrayPool<byte> _bytePool = ArrayPool<byte>.Shared;
    private static readonly ConcurrentDictionary<string, IntPtr> _stringCache = new();
    private static readonly ConcurrentQueue<IntPtr> _stringPtrPool = new();

    // 小さな文字列のしきい値（stackallocを使用）
    private const int STACKALLOC_THRESHOLD = 256;

    // キャッシュする文字列の最大長
    private const int CACHE_MAX_LENGTH = 64;

    // キャッシュの最大サイズ
    private const int MAX_CACHE_SIZE = 1000;

    /// <summary>
    /// ネイティブポインタからマネージド文字列に変換
    /// ArrayPoolを使用してメモリ割り当てを最適化
    /// </summary>
    public static unsafe string NativeToManaged(IntPtr pointer)
    {
      if (pointer == IntPtr.Zero) return null;

      // 長さを効率的に計算
      var len = 0;
      byte* ptr = (byte*)pointer;
      while (ptr[len] != 0) len++;

      if (len == 0) return string.Empty;

      // 小さな文字列にはstackallocを使用
      if (len <= STACKALLOC_THRESHOLD)
      {
        var buffer = stackalloc byte[len];
        Buffer.MemoryCopy(ptr, buffer, len, len);
        return Encoding.UTF8.GetString(new ReadOnlySpan<byte>(buffer, len));
      }

      // 大きな文字列にはArrayPoolを使用
      var pooledBuffer = _bytePool.Rent(len);
      try
      {
        Marshal.Copy(pointer, pooledBuffer, 0, len);
        return Encoding.UTF8.GetString(pooledBuffer, 0, len);
      }
      finally
      {
        _bytePool.Return(pooledBuffer);
      }
    }

    /// <summary>
    /// マネージド文字列をネイティブポインタに変換
    /// String Pooling、stackalloc、ArrayPoolを組み合わせて最適化
    /// </summary>
    public static unsafe IntPtr ManagedToNative(string managedObj, ConcurrentQueue<Action> finalizers)
    {
      if (managedObj == null) return IntPtr.Zero;
      if (managedObj.Length == 0)
      {
        // 空文字列用の静的ポインタを返す
        var emptyPtr = Marshal.AllocHGlobal(1);
        Marshal.WriteByte(emptyPtr, 0);
        finalizers?.Enqueue(() => Marshal.FreeHGlobal(emptyPtr));
        return emptyPtr;
      }

      // キャッシュから検索（短い文字列のみ）
      if (managedObj.Length <= CACHE_MAX_LENGTH && _stringCache.Count < MAX_CACHE_SIZE)
      {
        if (_stringCache.TryGetValue(managedObj, out var cachedPtr))
        {
          return cachedPtr;
        }
      }

      var maxBytes = Encoding.UTF8.GetMaxByteCount(managedObj.Length);
      IntPtr nativePtr;

      // 小さな文字列にはstackallocを使用
      if (maxBytes <= STACKALLOC_THRESHOLD)
      {
        var buffer = stackalloc byte[maxBytes + 1];
        var actualBytes = Encoding.UTF8.GetBytes(managedObj, new Span<byte>(buffer, maxBytes));
        buffer[actualBytes] = 0; // null終端

        nativePtr = Marshal.AllocHGlobal(actualBytes + 1);
        Buffer.MemoryCopy(buffer, (void*)nativePtr, actualBytes + 1, actualBytes + 1);
      }
      else
      {
        // 大きな文字列にはArrayPoolを使用
        var pooledBuffer = _bytePool.Rent(maxBytes + 1);
        try
        {
          var actualBytes = Encoding.UTF8.GetBytes(managedObj, pooledBuffer);
          pooledBuffer[actualBytes] = 0; // null終端

          nativePtr = Marshal.AllocHGlobal(actualBytes + 1);
          Marshal.Copy(pooledBuffer, 0, nativePtr, actualBytes + 1);
        }
        finally
        {
          _bytePool.Return(pooledBuffer);
        }
      }

      // 短い文字列はキャッシュに保存
      if (managedObj.Length <= CACHE_MAX_LENGTH && _stringCache.Count < MAX_CACHE_SIZE)
      {
        _stringCache.TryAdd(managedObj, nativePtr);
      }
      else
      {
        // キャッシュしない場合はファイナライザーに追加
        finalizers?.Enqueue(() => Marshal.FreeHGlobal(nativePtr));
      }

      return nativePtr;
    }

    /// <summary>
    /// キャッシュをクリア（メモリリーク防止）
    /// 以下のタイミングで呼び出すことを推奨：
    /// 1. プラグインのアンロード時
    /// 2. マップ変更時
    /// 3. 定期的なメンテナンス（例：1時間ごと）
    /// 4. メモリ使用量が閾値を超えた時
    /// </summary>
    public static void ClearCache()
    {
      foreach (var kvp in _stringCache)
      {
        Marshal.FreeHGlobal(kvp.Value);
      }
      _stringCache.Clear();
    }

    /// <summary>
    /// 条件付きキャッシュクリア
    /// キャッシュサイズが指定した閾値を超えた場合のみクリア
    /// </summary>
    public static bool ClearCacheIfNeeded(int threshold = 800)
    {
      if (_stringCache.Count >= threshold)
      {
        ClearCache();
        return true;
      }
      return false;
    }

    /// <summary>
    /// 古いキャッシュエントリを部分的にクリア
    /// LRU（Least Recently Used）的な動作をシミュレート
    /// </summary>
    public static void TrimCache(int targetSize = 500)
    {
      if (_stringCache.Count <= targetSize) return;

      var itemsToRemove = _stringCache.Count - targetSize;
      var keysToRemove = new List<string>();

      foreach (var kvp in _stringCache)
      {
        keysToRemove.Add(kvp.Key);
        if (keysToRemove.Count >= itemsToRemove) break;
      }

      foreach (var key in keysToRemove)
      {
        if (_stringCache.TryRemove(key, out var ptr))
        {
          Marshal.FreeHGlobal(ptr);
        }
      }
    }

    /// <summary>
    /// キャッシュ統計を取得
    /// </summary>
    public static (int Count, int MaxSize) GetCacheStats()
    {
      return (_stringCache.Count, MAX_CACHE_SIZE);
    }
  }
}