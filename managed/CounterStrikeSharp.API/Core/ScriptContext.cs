/*
 * Copyright (c) 2014 Bas Timmer/NTAuthority et al.
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
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 *
 * This file has been modified from its original form for use in this program
 * under GNU Lesser General Public License, version 2.
 */

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading;
using CounterStrikeSharp.API.Modules.Utils;
using Cysharp.Text;
using Utf8StringInterpolation;

namespace CounterStrikeSharp.API.Core
{
	public class NativeException : Exception
	{
		public NativeException(string message) : base(message)
		{
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	[Serializable]
	public unsafe struct fxScriptContext
	{
		public int numArguments;
		public int numResults;
		public int hasError;

		public ulong nativeIdentifier;
		public fixed byte functionData[8 * 32];
		public fixed byte result[8];
	}

	public class ScriptContext
	{
		[ThreadStatic] private static ScriptContext _globalScriptContext;

		public static ScriptContext GlobalScriptContext
		{
			get
			{
				if (_globalScriptContext == null) _globalScriptContext = new ScriptContext();
				return _globalScriptContext;
			}
		}

		public unsafe ScriptContext()
		{
		}

		public unsafe ScriptContext(fxScriptContext* context)
		{
			m_extContext = *context;
		}

		private readonly ConcurrentQueue<IntPtr> ms_finalizers = new ConcurrentQueue<IntPtr>();

		private readonly object ms_lock = new object();

		internal object Lock => ms_lock;

		public fxScriptContext m_extContext = new fxScriptContext();

		internal bool isCleanupLocked = false;

		[SecuritySafeCritical]
		public void Reset()
		{
			InternalReset();
		}

		[SecurityCritical]
		private void InternalReset()
		{
			m_extContext.numArguments = 0;
			m_extContext.numResults = 0;
			m_extContext.hasError = 0;
			//CleanUp();
		}

		[SecuritySafeCritical]
		public void Invoke()
		{
			if (!isCleanupLocked)
			{
				isCleanupLocked = true;
				InvokeNativeInternal();
				GlobalCleanUp();
				isCleanupLocked = false;
				return;
			}

			InvokeNativeInternal();
		}

		[SecurityCritical]
		private void InvokeNativeInternal()
		{
			unsafe
			{
				fixed (fxScriptContext* cxt = &m_extContext)
				{
					Helpers.InvokeNative(new IntPtr(cxt));
				}
			}
		}

		public unsafe byte[] GetBytes()
		{
			fixed (fxScriptContext* context = &m_extContext)
			{
				byte[] arr = new byte[8 * 32];
				Marshal.Copy((IntPtr)context->functionData, arr, 0, 8 * 32);

				return arr;
			}
		}

		public unsafe IntPtr GetContextUnderlyingAddress()
		{
			fixed (fxScriptContext* context = &m_extContext)
			{
				return (IntPtr)context;
			}
		}

		[SecuritySafeCritical]
		public void Push(object arg)
		{
			PushInternal(arg);
		}

		[SecuritySafeCritical]
		public unsafe void SetResult(object arg, fxScriptContext* cxt)
		{
			SetResultInternal(cxt, arg);
		}

		[SecurityCritical]
		private unsafe void PushInternal(object arg)
		{
			if (arg is null) arg = 0;

			if (arg is Enum en) arg = EnumToUnderlying(en);

			if (arg is string s) { PushString(s); return; }
			if (arg is InputArgument ia) { Push(ia.Value); return; }
			if (arg is IMarshalToNative mt) { foreach (var v in mt.GetNativeObject()) Push(v); return; }
			if (arg is NativeObject no) { Push((InputArgument)no); return; }
			if (arg is NativeEntity ne) { Push((InputArgument)ne); return; }

			fixed (fxScriptContext* ctx = &m_extContext)
			{
				if (IsPrimitive8(arg))
				{
					PushUnsafeZ(ctx, arg);
				}
				else
				{
					Marshal.StructureToPtr(arg, new IntPtr(ctx->functionData + 8 * ctx->numArguments), true);
				}
				ctx->numArguments++;
			}
		}

		[SecurityCritical]
		public unsafe void SetIdentifier(ulong arg)
		{
			fixed (fxScriptContext* context = &m_extContext)
			{
				context->nativeIdentifier = arg;
			}
		}

		public unsafe void CheckErrors()
		{
			fixed (fxScriptContext* context = &m_extContext)
			{
				if (Convert.ToBoolean(context->hasError))
				{
					string error = GetResult<string>();
					Reset();
					throw new NativeException(error);
				}
			}
		}

		[SecurityCritical]
		internal unsafe void Push(fxScriptContext* context, object arg)
		{
			if (arg == null)
			{
				arg = 0;
			}

			if (arg.GetType().IsEnum)
			{
				arg = Convert.ChangeType(arg, arg.GetType().GetEnumUnderlyingType());
			}

			if (arg is string)
			{
				var str = (string)Convert.ChangeType(arg, typeof(string));
				PushString(context, str);

				return;
			}
			else if (arg is InputArgument ia)
			{
				Push(context, ia.Value);

				return;
			}
			else if (arg is IMarshalToNative marshalToNative)
			{
				foreach (var value in marshalToNative.GetNativeObject())
				{
					Push(context, value);
				}

				return;
			}
			else if (arg is NativeObject nativeObject)
			{
				Push(context, (InputArgument)nativeObject);
				return;
			}
			else if (arg is NativeEntity nativeValue)
			{
				Push(context, (InputArgument)nativeValue);
				return;
			}

			if (Marshal.SizeOf(arg.GetType()) <= 8)
			{
				PushUnsafe(context, arg);
			}

			context->numArguments++;
		}

		[SecurityCritical]
		internal unsafe void SetResultInternal(fxScriptContext* ctx, object arg)
		{
			if (arg is null) arg = 0;
			if (arg is Enum en) arg = EnumToUnderlying(en);

			if (arg is string s) { SetResultString(ctx, s); return; }
			if (arg is InputArgument ia) { SetResultInternal(ctx, ia.Value); return; }

			if (IsPrimitive8(arg))
				SetResultUnsafeZ(ctx, arg);
			else
				Marshal.StructureToPtr(arg, new IntPtr(ctx->result), true);
		}

		[SecurityCritical]
		internal unsafe void PushUnsafe(fxScriptContext* cxt, object arg)
		{
			//*(long*)(&cxt->functionData[8 * cxt->numArguments]) = 0;
			//Marshal.StructureToPtr(arg, new IntPtr(cxt->functionData + (8 * cxt->numArguments)), true);
			PushUnsafeZ(cxt, arg);
		}

		[SecurityCritical]
		internal unsafe void SetResultUnsafe(fxScriptContext* cxt, object arg)
		{
			//*(long*)(&cxt->result[0]) = 0;
			//Marshal.StructureToPtr(arg, new IntPtr(cxt->result), true);
			SetResultUnsafeZ(cxt, arg);
		}

		[SecurityCritical]
		internal unsafe void PushUnsafeZ(fxScriptContext* cxt, object arg)
		{
			byte* slot = cxt->functionData + 8 * cxt->numArguments;

			switch (arg)
			{
				case int v: WriteValue(slot, v); break;
				case uint v: WriteValue(slot, v); break;
				case long v: WriteValue(slot, v); break;
				case ulong v: WriteValue(slot, v); break;
				case float v: WriteValue(slot, v); break;
				case double v: WriteValue(slot, v); break;
				case bool v: WriteValue(slot, v ? (byte)1 : (byte)0); break;
				case IntPtr v: WriteValue(slot, v); break;
				case byte v: WriteValue(slot, v); break;
				case sbyte v: WriteValue(slot, v); break;
				case char v: WriteValue(slot, v); break;
				case short v: WriteValue(slot, v); break;
				case ushort v: WriteValue(slot, v); break;
				default:
					Marshal.StructureToPtr(arg, new IntPtr(slot), true); break;
			}
		}

		[SecurityCritical]
		internal unsafe void SetResultUnsafeZ(fxScriptContext* cxt, object arg)
		{
			byte* slot = cxt->result;

			switch (arg)
			{
				case int v: WriteValue(slot, v); break;
				case uint v: WriteValue(slot, v); break;
				case long v: WriteValue(slot, v); break;
				case ulong v: WriteValue(slot, v); break;
				case float v: WriteValue(slot, v); break;
				case double v: WriteValue(slot, v); break;
				case bool v: WriteValue(slot, v ? (byte)1 : (byte)0); break;
				case IntPtr v: WriteValue(slot, v); break;
				case byte v: WriteValue(slot, v); break;
				case sbyte v: WriteValue(slot, v); break;
				case char v: WriteValue(slot, v); break;
				case short v: WriteValue(slot, v); break;
				case ushort v: WriteValue(slot, v); break;
				default:
					Marshal.StructureToPtr(arg, new IntPtr(slot), true); break;
			}
		}

		[SecurityCritical]
		internal unsafe void PushString(string str)
		{
			fixed (fxScriptContext* cxt = &m_extContext)
			{
				PushString(cxt, str);
			}
		}

		[SecurityCritical]
		internal unsafe void PushString(fxScriptContext* cxt, string str)
		{
			var ptr = IntPtr.Zero;
			ptr = Utf8Interop.AllocUtf8Ultra(str);

			if (str != null)
			{
				ms_finalizers.Enqueue(ptr);
			}

			unsafe
			{
				*(IntPtr*)(&cxt->functionData[8 * cxt->numArguments]) = ptr;
			}

			cxt->numArguments++;
		}

		[SecurityCritical]
		internal unsafe void SetResultString(fxScriptContext* cxt, string str)
		{
			var ptr = IntPtr.Zero;
			ptr = Utf8Interop.AllocUtf8Ultra(str);

			if (str != null)
			{
				ms_finalizers.Enqueue(ptr);
			}

			unsafe
			{
				*(IntPtr*)(&cxt->result[8]) = ptr;
			}
		}

		[SecuritySafeCritical]
		private unsafe void Free(IntPtr ptr)
		{
			if (ptr != IntPtr.Zero)
			{
				NativeMemory.Free((void*)ptr);
			}
		}

		[SecuritySafeCritical]
		public T GetArgument<T>(int index)
		{
			return (T)GetArgument(typeof(T), index);
		}

		[SecuritySafeCritical]
		public object GetArgument(Type type, int index)
		{
			return GetArgumentHelper(type, index);
		}

		[SecurityCritical]
		internal unsafe object GetArgument(fxScriptContext* cxt, Type type, int index)
		{
			return GetArgumentHelper(cxt, type, index);
		}

		[SecurityCritical]
		private unsafe object GetArgumentHelper(Type type, int index)
		{
			fixed (fxScriptContext* cxt = &m_extContext)
			{
				return GetArgumentHelper(cxt, type, index);
			}
		}

		[SecurityCritical]
		private unsafe object GetArgumentHelper(fxScriptContext* context, Type type, int index)
		{
			return GetResult(type, &context->functionData[index * 8]);
		}

		[SecuritySafeCritical]
		public T GetResult<T>()
		{
			return (T)GetResult(typeof(T));
		}

		[SecuritySafeCritical]
		public object GetResult(Type type)
		{
			return GetResultHelper(type);
		}

		[SecurityCritical]
		internal unsafe object GetResult(fxScriptContext* cxt, Type type)
		{
			return GetResultHelper(cxt, type);
		}

		[SecurityCritical]
		private unsafe object GetResultHelper(Type type)
		{
			fixed (fxScriptContext* cxt = &m_extContext)
			{
				return GetResultHelper(cxt, type);
			}
		}

		[SecurityCritical]
		private unsafe object GetResultHelper(fxScriptContext* context, Type type)
		{
			return GetResult(type, &context->result[0]);
		}

		[SecurityCritical]
		internal unsafe object GetResult(Type type, byte* ptr)
		{
			if (type == typeof(string))
			{
				var nativeUtf8 = *(IntPtr*)&ptr[0];
				return Utf8PtrToString((byte*)nativeUtf8);
			}

			if (typeof(NativeObject).IsAssignableFrom(type))
			{
				var pointer = (IntPtr)GetResult(typeof(IntPtr), ptr);
				return Activator.CreateInstance(type, pointer);
			}

			if (type == typeof(object))
			{
				// var dataPtr = *(IntPtr*)&ptr[0];
				// var dataLength = *(long*)&ptr[8];
				//
				// byte[] data = new byte[dataLength];
				// Marshal.Copy(dataPtr, data, 0, (int)dataLength);

				return null;
				//return MsgPackDeserializer.Deserialize(data);
			}

			if (type.IsEnum)
			{
				return Enum.ToObject(type, GetResult(type.GetEnumUnderlyingType(), ptr));
			}

			if (Marshal.SizeOf(type) <= 8)
			{
				return GetResultInternal(type, ptr);
			}

			return null;
		}

		[SecurityCritical]
		private unsafe object GetResultInternal(Type type, byte* ptr)
		{
			//return Unsafe.ReadUnaligned<object>(ref *ptr);
			if (type == typeof(int))
			{
				return ReadValue<int>(ptr);
			}
			else if (type == typeof(uint))
			{
				return ReadValue<uint>(ptr);
			}
			else if (type == typeof(long))
			{
				return ReadValue<long>(ptr);
			}
			else if (type == typeof(ulong))
			{
				return ReadValue<ulong>(ptr);
			}
			else if (type == typeof(float))
			{
				return ReadValue<float>(ptr);
			}
			else if (type == typeof(double))
			{
				return ReadValue<double>(ptr);
			}
			else if (type == typeof(bool))
			{
				return ReadValue<byte>(ptr) != 0;
			}
			else if (type == typeof(IntPtr))
			{
				return new IntPtr(ReadValue<long>(ptr));
			}

			var obj = Marshal.PtrToStructure(new IntPtr(ptr), type);
			return obj;
		}

		[SecurityCritical]
		public unsafe string ErrorHandler(byte* error)
		{
			if (error != null)
			{
				return Utf8PtrToString(error);
			}

			return "Native invocation failed.";
		}

		internal void GlobalCleanUp()
		{
			const int TimeBudgetUs = 500; // 0.5 ms - increased budget
			var sw = Stopwatch.GetTimestamp();
			long ticksBudget = TimeBudgetUs * Stopwatch.Frequency / 1_000_000;
			int processedCount = 0;
			const int MaxProcessPerFrame = 100; // Limit processing per frame

			while (ms_finalizers.TryDequeue(out var ptr) && processedCount < MaxProcessPerFrame)
			{
				Free(ptr);
				processedCount++;

				// Check time budget every 10 items to reduce overhead
				if (processedCount % 10 == 0 && Stopwatch.GetTimestamp() - sw > ticksBudget)
					break;
			}
		}

		public override string ToString()
		{
			return ZString.Format("ScriptContext{{numArgs={0}}}", m_extContext.numArguments);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static bool IsPrimitive8(object v) =>
				v is int or uint or long or ulong or float or double or bool or IntPtr;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static object EnumToUnderlying(Enum e)
		{
			TypeCode code = Type.GetTypeCode(Enum.GetUnderlyingType(e.GetType()));
			return code switch
			{
				TypeCode.Int32 => ((IConvertible)e).ToInt32(null),
				TypeCode.UInt32 => ((IConvertible)e).ToUInt32(null),
				TypeCode.Int64 => ((IConvertible)e).ToInt64(null),
				_ => ((IConvertible)e).ToUInt64(null),
			};
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		static unsafe void WriteValue<T>(byte* dst, T value) where T : unmanaged
		{
			Debug.Assert(((nuint)dst % (uint)Unsafe.SizeOf<T>()) == 0);

			ref byte r = ref Unsafe.AsRef<byte>(dst);
			// For 8-byte primitives, zero-clearing is redundant.
			// For types smaller than 8 bytes, we clear the slot to prevent garbage in the upper bytes.
			if (Unsafe.SizeOf<T>() < 8)
			{
				*(long*)dst = 0;
			}
			Unsafe.WriteUnaligned(dst, value);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		static unsafe T ReadValue<T>(byte* src) where T : unmanaged
				=> Unsafe.ReadUnaligned<T>(src);

		public static class Utf8Interop
		{
			// Removed buffer caching to eliminate race conditions and memory safety issues
			// Direct allocation/deallocation is more predictable and safer

			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			internal static unsafe IntPtr AllocUtf8(string? str)
			{
				if (str is null) return IntPtr.Zero;

				const int StackLimit = 256; // byte
				int maxLen = str.Length * 3;
				Span<byte> utf8Buf = maxLen <= StackLimit
								? stackalloc byte[StackLimit]
								: ArrayPool<byte>.Shared.Rent(maxLen);

				int written = Encoding.UTF8.GetBytes(str, utf8Buf);
				var ptr = (byte*)System.Runtime.InteropServices.NativeMemory.Alloc((nuint)(written + 1));
				utf8Buf.Slice(0, written).CopyTo(new Span<byte>(ptr, written));
				ptr[written] = 0;

				if (utf8Buf.Length > StackLimit)
					ArrayPool<byte>.Shared.Return(utf8Buf.ToArray());

				return (IntPtr)ptr;
			}

			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			public static unsafe IntPtr AllocUtf8Ultra(string? str)
			{
				if (str is null) return IntPtr.Zero;

				var maxBytes = Encoding.UTF8.GetMaxByteCount(str.Length) + 1;
				var ptr = (byte*)NativeMemory.Alloc((nuint)maxBytes);

				if (ptr == null)
				{
					throw new OutOfMemoryException("Failed to allocate UTF8 buffer");
				}

				Span<byte> dst = new Span<byte>(ptr, maxBytes);
				if (!Utf8String.TryFormat(dst, out var written, $"{str}"))
				{
					NativeMemory.Free(ptr);
					throw new InvalidOperationException("Failed to format UTF8 string");
				}

				ptr[written] = 0;
				return (IntPtr)ptr;
			}

		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static unsafe string Utf8PtrToString(byte* src)
		{
			if (src is null) return null;

			byte* p = src;
			while (*p != 0) ++p;
			int len = (int)(p - src);
			return Encoding.UTF8.GetString(new ReadOnlySpan<byte>(src, len));
		}
	}
}
