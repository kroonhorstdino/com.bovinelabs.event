// <copyright file="UnsafeEventStream.Writer.cs" company="BovineLabs">
//     Copyright (c) BovineLabs. All rights reserved.
// </copyright>

namespace BovineLabs.Event.Containers
{
    using System.Diagnostics.CodeAnalysis;
    using Unity.Collections;
    using Unity.Collections.LowLevel.Unsafe;

    public unsafe partial struct UnsafeEventStream
    {
        /// <summary> The writer instance. </summary>
        public struct Writer
        {
            [NativeDisableUnsafePtrRestriction]
            private UnsafeEventStreamBlockData* blockStream;

#pragma warning disable SA1308
            [NativeSetThreadIndex]
            [SuppressMessage("ReSharper", "InconsistentNaming", Justification = "Required by unity scheduler")] // TODO is this true?
            private int m_ThreadIndex;
#pragma warning restore SA1308

            internal Writer(ref UnsafeEventStream stream)
            {
                this.blockStream = stream.blockData;
                this.m_ThreadIndex = 0; // 0 so main thread works
            }

            /// <summary> Write data. </summary>
            /// <typeparam name="T">The type of value.</typeparam>
            /// <param name="value">Value to write.</param>
            [BurstCompatible(GenericTypeArguments = new[] { typeof(int) })]
            public void Write<T>(T value)
                where T : struct
            {
                ref var dst = ref this.Allocate<T>();
                dst = value;
            }

            /// <summary> Allocate space for data. </summary>
            /// <typeparam name="T">The type of value.</typeparam>
            /// <returns>Reference to allocated space for data.</returns>
            [BurstCompatible(GenericTypeArguments = new[] { typeof(int) })]
            public ref T Allocate<T>()
                where T : struct
            {
                var size = UnsafeUtility.SizeOf<T>();
                return ref UnsafeUtility.AsRef<T>(this.Allocate(size));
            }

            /// <summary> Allocate space for data. </summary>
            /// <param name="size">Size in bytes.</param>
            /// <returns>Pointer to allocated space for data.</returns>
            public byte* Allocate(int size)
            {
                var threadIndex = CollectionHelper.AssumeThreadRange(this.m_ThreadIndex);

                var ranges = this.blockStream->Ranges + threadIndex;

                var ptr = ranges->CurrentPtr;
                var allocationEnd = ptr + size;

                ranges->CurrentPtr = allocationEnd;

                if (allocationEnd > ranges->CurrentBlockEnd)
                {
                    var oldBlock = ranges->CurrentBlock;
                    var newBlock = this.blockStream->Allocate(oldBlock, threadIndex);

                    ranges->CurrentBlock = newBlock;
                    ranges->CurrentPtr = newBlock->Data;

                    if (ranges->Block == null)
                    {
                        ranges->OffsetInFirstBlock = (int)(newBlock->Data - (byte*)newBlock);
                        ranges->Block = newBlock;
                    }
                    else
                    {
                        ranges->NumberOfBlocks++;
                    }

                    ranges->CurrentBlockEnd = (byte*)newBlock + UnsafeEventStreamBlockData.AllocationSize;

                    ptr = newBlock->Data;
                    ranges->CurrentPtr = newBlock->Data + size;
                }

                ranges->ElementCount++;
                ranges->LastOffset = (int)(ranges->CurrentPtr - (byte*)ranges->CurrentBlock);

                return ptr;
            }
        }
    }
}