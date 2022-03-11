// <copyright file="UnsafeEventStream.cs" company="BovineLabs">
//     Copyright (c) BovineLabs. All rights reserved.
// </copyright>

namespace BovineLabs.Event.Containers
{
    using System;
    using Unity.Burst;
    using Unity.Collections;
    using Unity.Collections.LowLevel.Unsafe;
    using Unity.Jobs;
    using Unity.Jobs.LowLevel.Unsafe;

    /// <summary>
    /// A data streaming supporting parallel reading and parallel writings, without any thread safety check features.
    /// Allows you to write different types or arrays into a single stream.
    /// </summary>
    public unsafe partial struct UnsafeEventStream : INativeDisposable, IEquatable<UnsafeEventStream>
    {
        /// <summary> Gets the number of streams the list can use. </summary>
        public const int ForEachCount = JobsUtility.MaxJobThreadCount;

        [NativeDisableUnsafePtrRestriction]
        private UnsafeEventStreamBlockData* blockData;

        private Allocator allocator;

        /// <summary> Initializes a new instance of the <see cref="UnsafeEventStream"/> struct. </summary>
        /// <param name="allocator"> The specified type of memory allocation. </param>
        public UnsafeEventStream(Allocator allocator)
        {
            AllocateBlock(out this, allocator);
            this.AllocateForEach();
        }

        /// <summary> Gets a value indicating whether memory for the container is allocated. </summary>
        /// <value> True if this container object's internal storage has been allocated. </value>
        /// <remarks>
        /// <para> Note that the container storage is not created if you use the default constructor.
        /// You must specify at least an allocation type to construct a usable container. </para>
        /// </remarks>
        public bool IsCreated => this.blockData != null;

        /// <summary> Reports whether container is empty. </summary>
        /// <returns> True if this container empty. </returns>
        public bool IsEmpty()
        {
            if (!this.IsCreated)
            {
                return true;
            }

            for (var i = 0; i != ForEachCount; i++)
            {
                if (this.blockData->Ranges[i].ElementCount > 0)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary> Returns reader instance. </summary>
        /// <returns> Reader instance. </returns>
        public Reader AsReader()
        {
            return new Reader(ref this);
        }

        /// <summary> Returns a writer instance. </summary>
        /// <returns> Writer instance. </returns>
        public Writer AsWriter()
        {
            return new Writer(ref this);
        }

        /// <summary>
        /// The current number of items in the container.
        /// </summary>
        /// <returns>The item count.</returns>
        public int Count()
        {
            var itemCount = 0;

            for (var i = 0; i != ForEachCount; i++)
            {
                itemCount += this.blockData->Ranges[i].ElementCount;
            }

            return itemCount;
        }

        /// <summary> Copies stream data into NativeArray. </summary>
        /// <typeparam name="T"> The type of value. </typeparam>
        /// <param name="arrayAllocator"> A member of the <see cref="Allocator"/> enumeration. </param>
        /// <returns> A new NativeArray, allocated with the given strategy and wrapping the stream data. </returns>
        /// <remarks> <para>The array is a copy of stream data.</para> </remarks>
        [BurstCompatible(GenericTypeArguments = new[] { typeof(int) })]
        public NativeArray<T> ToNativeArray<T>(Allocator arrayAllocator)
            where T : struct
        {
            var array = new NativeArray<T>(this.Count(), arrayAllocator, NativeArrayOptions.UninitializedMemory);
            var reader = this.AsReader();

            var offset = 0;
            for (var i = 0; i != reader.ForEachCount; i++)
            {
                reader.BeginForEachIndex(i);
                var rangeItemCount = reader.RemainingItemCount;
                for (var j = 0; j < rangeItemCount; ++j)
                {
                    array[offset] = reader.Read<T>();
                    offset++;
                }

                reader.EndForEachIndex();
            }

            return array;
        }

        /// <summary> Disposes of this stream and deallocates its memory immediately. </summary>
        public void Dispose()
        {
            this.Deallocate();
        }

        /// <inheritdoc/>
        public bool Equals(UnsafeEventStream other)
        {
            return this.blockData == other.blockData;
        }

        /// <summary> Safely disposes of this container and deallocates its memory when the jobs that use it have completed. </summary>
        /// <remarks>
        /// <para>You can call this function dispose of the container immediately after scheduling the job. Pass
        /// the <see cref="JobHandle"/> returned by the Job.Schedule method using the `jobHandle` parameter so the job scheduler can dispose the container
        /// after all jobs using it have run.</para>
        /// </remarks>
        /// <param name="inputDeps">All jobs spawned will depend on this JobHandle.</param>
        /// <returns>A new job handle containing the prior handles as well as the handle for the job that deletes
        /// the container.</returns>
        [BurstCompatible(RequiredUnityDefine = "UNITY_2020_2_OR_NEWER") /* Due to job scheduling on 2020.1 using statics */]
        public JobHandle Dispose(JobHandle inputDeps)
        {
            var jobHandle = new DisposeJob { Container = this }.Schedule(inputDeps);
            this.blockData = null;
            return jobHandle;
        }

        internal static void AllocateBlock(out UnsafeEventStream stream, Allocator allocator)
        {
            var allocationSize = sizeof(UnsafeEventStreamBlockData) + (sizeof(UnsafeEventStreamBlock*) * ForEachCount);
            var buffer = (byte*)UnsafeUtility.Malloc(allocationSize, 16, allocator);
            UnsafeUtility.MemClear(buffer, allocationSize);

            var block = (UnsafeEventStreamBlockData*)buffer;

            stream.blockData = block;
            stream.allocator = allocator;

            block->Allocator = allocator;
            block->Blocks = (UnsafeEventStreamBlock**)(buffer + sizeof(UnsafeEventStreamBlockData));

            block->Ranges = null;
            block->ThreadRanges = null;
        }

        internal void AllocateForEach()
        {
            long allocationSize = sizeof(UnsafeEventStreamRange) * ForEachCount;
            long allocationThreadSize = sizeof(UnsafeEventStreamThreadRange) * ForEachCount;
            this.blockData->Ranges = (UnsafeEventStreamRange*)UnsafeUtility.Malloc(allocationSize, 16, this.allocator);
            this.blockData->ThreadRanges = (UnsafeEventStreamThreadRange*)UnsafeUtility.Malloc(allocationThreadSize, 16, this.allocator);
            UnsafeUtility.MemClear(this.blockData->Ranges, allocationSize);
            UnsafeUtility.MemClear(this.blockData->ThreadRanges, allocationThreadSize);
        }

        private void Deallocate()
        {
            if (this.blockData == null)
            {
                return;
            }

            for (var i = 0; i != ForEachCount; i++)
            {
                var block = this.blockData->Blocks[i];
                while (block != null)
                {
                    var next = block->Next;
                    UnsafeUtility.Free(block, this.allocator);
                    block = next;
                }
            }

            UnsafeUtility.Free(this.blockData->Ranges, this.allocator);
            UnsafeUtility.Free(this.blockData->ThreadRanges, this.allocator);
            UnsafeUtility.Free(this.blockData, this.allocator);
            this.blockData = null;
            this.allocator = Allocator.None;
        }

        [BurstCompile]
        private struct DisposeJob : IJob
        {
            public UnsafeEventStream Container;

            public void Execute()
            {
                this.Container.Deallocate();
            }
        }
    }
}