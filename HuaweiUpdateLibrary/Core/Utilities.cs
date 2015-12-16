﻿using System;
using System.IO;
using System.Runtime.InteropServices;

namespace HuaweiUpdateLibrary.Core
{
    internal static class Utilities
    {
        public static Int32 UintSize = Marshal.SizeOf(typeof(UInt32));

        public static bool ByteToType<T>(BinaryReader reader, out T result)
        {
            var objSize = Marshal.SizeOf(typeof(T));
            var bytes = reader.ReadBytes(objSize);
            if (bytes.Length == 0 || bytes.Length != objSize)
            {
                result = default(T);
                return false;
            }

            var ptr = Marshal.AllocHGlobal(objSize);

            try
            {
                Marshal.Copy(bytes, 0, ptr, objSize);
                result = (T)Marshal.PtrToStructure(ptr, typeof(T));
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }

            return true;
        }

        public static bool TypeToByte<T>(T type, out byte[] result)
        {
            var objSize = Marshal.SizeOf(typeof(T));
            result = new byte[objSize];
            var ptr = Marshal.AllocHGlobal(objSize);

            try
            {
                Marshal.StructureToPtr(type, ptr, true);
                Marshal.Copy(ptr, result, 0, objSize);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }

            return true;
        }
    }
}
