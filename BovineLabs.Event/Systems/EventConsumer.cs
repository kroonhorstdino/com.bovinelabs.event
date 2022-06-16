// <copyright file="EventConsumer.cs" company="BovineLabs">
//     Copyright (c) BovineLabs. All rights reserved.
// </copyright>

namespace BovineLabs.Event.Systems
{
    using System;
    using System.Diagnostics;
    using BovineLabs.Event.Containers;
    using BovineLabs.Event.Jobs;
    using Unity.Assertions;
    using Unity.Collections;
    using Unity.Collections.LowLevel.Unsafe;
    using Unity.Jobs;

    public unsafe struct EventConsumer<T>
        where T : unmanaged
    {
        internal Consumer* Consumer;

        public int ReadersCount => this.Consumer->Readers.Length;

        public bool HasReaders => this.Consumer->Readers.Length > 0;

        public JobHandle GetReaders(JobHandle jobHandle, out UnsafeReadArray<NativeEventStream.Reader> readers, Allocator allocator = Allocator.Temp)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            CheckAllocateArguments(allocator);
            this.Consumer->ReadersRequested++;
#endif

            var length = this.Consumer->Readers.Length;
            var size = UnsafeUtility.SizeOf<NativeEventStream.Reader>() * length;
            var buffer = UnsafeUtility.Malloc(size, UnsafeUtility.AlignOf<NativeEventStream.Reader>(), allocator);

            for (var i = 0; i < length; i++)
            {
                UnsafeUtility.WriteArrayElement(buffer, i, this.Consumer->Readers[i].AsReader());
            }

            readers = new UnsafeReadArray<NativeEventStream.Reader>(buffer, length, allocator);

            return JobHandle.CombineDependencies(jobHandle, this.Consumer->InputHandle);
        }

        public void AddJobHandle(JobHandle handle)
        {
            this.Consumer->JobHandle = handle;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            this.Consumer->HandleSet++;
#endif
        }

        /// <summary>
        /// Ensure a <see cref="NativeHashMap{TKey,TValue}" /> has the capacity to be filled with all events of a specific type.
        /// If the hash map already has elements, it will increase the size so that all events and existing elements can fit.
        /// </summary>
        /// <param name="handle"> Input dependencies. </param>
        /// <param name="hashMap"> The <see cref="NativeHashMap{TKey,TValue}"/> to ensure capacity of. </param>
        /// <typeparam name="TKey"> The key type of the <see cref="NativeHashMap{TKey,TValue}"/> . </typeparam>
        /// <typeparam name="TValue"> The value type of the <see cref="NativeHashMap{TKey,TValue}"/> . </typeparam>
        /// <returns> The dependency handle. </returns>
        public JobHandle EnsureHashMapCapacity<TKey, TValue>(JobHandle handle, NativeParallelHashMap<TKey, TValue> hashMap)
            where TKey : struct, IEquatable<TKey>
            where TValue : struct
        {
            if (this.ReadersCount != 0)
            {
                var counter = new NativeArray<int>(this.ReadersCount, Allocator.TempJob);
                handle = new EventConsumerJobs.CountJob { Counter = counter }.ScheduleParallel(this, handle);
                handle = new EventConsumerJobs.EnsureHashMapCapacityJob<TKey, TValue> { Counter = counter, HashMap = hashMap }.Schedule(handle);
                handle = counter.Dispose(handle);
            }

            return handle;
        }

        /// <summary>
        /// Ensure a <see cref="NativeMultiHashMap{TKey,TValue}" /> has the capacity to be filled with all events of a specific type.
        /// If the hash map already has elements, it will increase the size so that all events and existing elements can fit.
        /// </summary>
        /// <param name="handle"> Input dependencies. </param>
        /// <param name="hashMap"> The <see cref="NativeHashMap{TKey,TValue}"/> to ensure capacity of. </param>
        /// <typeparam name="TKey"> The key type of the <see cref="NativeHashMap{TKey,TValue}"/> . </typeparam>
        /// <typeparam name="TValue"> The value type of the <see cref="NativeHashMap{TKey,TValue}"/> . </typeparam>
        /// <returns> The dependency handle. </returns>
        public JobHandle EnsureHashMapCapacity<TKey, TValue>(JobHandle handle, NativeParallelMultiHashMap<TKey, TValue> hashMap)
            where TKey : struct, IEquatable<TKey>
            where TValue : struct
        {
            if (this.ReadersCount != 0)
            {
                var counter = new NativeArray<int>(this.ReadersCount, Allocator.TempJob);
                handle = new EventConsumerJobs.CountJob { Counter = counter }.ScheduleParallel(this, handle);
                handle = new EventConsumerJobs.EnsureMultiHashMapCapacityJob<TKey, TValue> { Counter = counter, HashMap = hashMap }.Schedule(handle);
                handle = counter.Dispose(handle);
            }

            return handle;
        }

        /// <summary> Get the total number of events of a specific type. </summary>
        /// <param name="handle"> Input dependencies. </param>
        /// <param name="count"> The output array. This must be length of at least 1 and the result will be stored in the index of 0. </param>
        /// <returns> The dependency handle. </returns>
        public JobHandle GetEventCount(JobHandle handle, NativeArray<int> count)
        {
            if (this.ReadersCount != 0)
            {
                var counter = new NativeArray<int>(this.ReadersCount, Allocator.TempJob);
                handle = new EventConsumerJobs.CountJob { Counter = counter }.ScheduleParallel(this, handle);
                handle = new EventConsumerJobs.SumJob { Counter = counter, Count = count }.Schedule(handle);
                counter.Dispose(handle);
            }

            return handle;
        }

        /// <summary> Writes all the events to a new NativeList. </summary>
        /// <param name="handle"> Input dependencies. </param>
        /// <param name="list"> The output list. </param>
        /// <param name="allocator"> The allocator to use on the list. Must be either TempJob or Persistent. </param>
        /// <returns> The dependency handle. </returns>
        public JobHandle ToNativeList(JobHandle handle, out NativeList<T> list, Allocator allocator = Allocator.TempJob)
        {
            Assert.AreNotEqual(Allocator.Temp, allocator, $"Use {Allocator.TempJob} or {Allocator.Persistent}");
            list = new NativeList<T>(128, allocator);
            handle = new EventConsumerJobs.ToNativeListJob<T> { List = list }.Schedule(this, handle);
            return handle;
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void CheckAllocateArguments(Allocator allocator)
        {
            if (allocator <= Allocator.None)
            {
                throw new ArgumentException("Allocator must be Temp, TempJob or Persistent", nameof(allocator));
            }
        }
    }

    internal struct Consumer
    {
        public UnsafeListPtr<NativeEventStream> Readers;
        public JobHandle JobHandle;
        public JobHandle InputHandle;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        public int ReadersRequested;
        public int HandleSet;
#endif
    }
}
