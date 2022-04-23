// <copyright file="NativeEventStreamExTests.cs" company="BovineLabs">
//     Copyright (c) BovineLabs. All rights reserved.
// </copyright>

#if BL_TESTING

namespace BovineLabs.Event.Tests.Containers
{
    using BovineLabs.Event.Containers;
    using BovineLabs.Testing;
    using NUnit.Framework;
    using Unity.Collections;
    using Unity.Collections.LowLevel.Unsafe;

    /// <summary> Tests for <see cref="NativeEventStreamEx"/> . </summary>
    public class NativeEventStreamExTests : ECSTestsFixture
    {
        /// <summary> Tests the extensions AllocateLarge and ReadLarge. </summary>
        /// <param name="size"> The size of the allocation. </param>
        [TestCase(512)] // less than max size
        [TestCase(4092)] // max size
        [TestCase(8192)] // requires just more than 2 blocks
        public unsafe void WriteRead(int size)
        {
            var stream = new NativeEventStream(Allocator.TempJob);

            var sourceData = new NativeArray<byte>(size, Allocator.Temp);
            for (var i = 0; i < size; i++)
            {
                sourceData[i] = (byte)(i % 255);
            }

            var writer = stream.AsWriter();
            writer.Write(size);
            NativeEventStreamEx.WriteLarge(ref writer, (byte*)sourceData.GetUnsafeReadOnlyPtr(), size);

            var reader = stream.AsReader();

            reader.BeginForEachIndex(0);

            var readSize = reader.Read<int>();

            Assert.AreEqual(size, readSize);

            var result = new NativeArray<byte>(readSize, Allocator.Temp);

            NativeEventStreamEx.ReadLarge(ref reader, (byte*)result.GetUnsafePtr(), readSize);

            reader.EndForEachIndex();

            for (var i = 0; i < readSize; i++)
            {
                Assert.AreEqual(sourceData[i], result[i]);
            }
        }
    }
}

#endif
