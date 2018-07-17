using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace CMMA
{
    internal unsafe class Program
    {   
        private const int Offset = -3;
        private const int CacheLineSize = 64;

        private const int NumberOfIterations = 1_000_000;

        private class TaskParameters
        {
            public volatile int* value;
            public volatile bool stopSignal;
            public volatile int readErrors;
        }

        private struct TestResult
        {
            public readonly long elapsedMilliseconds;
            public readonly int readErrors;

            public TestResult(long elapsedMilliseconds, int readErrors)
            {
                this.elapsedMilliseconds = elapsedMilliseconds;
                this.readErrors = readErrors;
            }
        }
        
        public static void Main(string[] args)
        {
            const int size = CacheLineSize * 2;
            
            // Allocating memory that can fit 2 cache lines
            var allocatedMemory = Marshal.AllocHGlobal(size);
            
            // To get the start of the second cache line in our Allocated memory, we first calculate "cacheLineIndex"
            // by divinding our memory pointer by "CacheLineSize" and adding 1.
            var cacheLineIndex = (long) allocatedMemory / CacheLineSize + 1;
            
            // Then we multiply the resulting cacheLineIndex by CacheLineSize, this will be the starting location of
            // the second cache line.
            var cacheLineStartAddress = cacheLineIndex * CacheLineSize;
            
            Console.WriteLine("Running benchmark...");
            
            var alignedResult = ReadWriteTest((int*) cacheLineStartAddress,
                ReaderThread,
                WriterThread
            );
            
            var unalignedResult = ReadWriteTest((int*) (cacheLineStartAddress + Offset),
                ReaderThread,
                WriterThread
            );

            var interlockedAlignedResult = ReadWriteTest((int*) cacheLineStartAddress,
                InterlockedReaderThread,
                InterlockedWriterThread
            );
            
            var interlockedUnalignedResult = ReadWriteTest((int*) (cacheLineStartAddress + Offset),
                InterlockedReaderThread,
                InterlockedWriterThread
            );
            
            Console.WriteLine("Aligned: {0} ms, read errors: {1}",
                alignedResult.elapsedMilliseconds,
                alignedResult.readErrors
            );
            
            Console.WriteLine("Unaligned: {0} ms, read errors: {1}",
                unalignedResult.elapsedMilliseconds,
                unalignedResult.readErrors
            );
            
            Console.WriteLine("Interlocked Aligned: {0} ms, read errors: {1}",
                interlockedAlignedResult.elapsedMilliseconds,
                interlockedAlignedResult.readErrors
            );
            
            Console.WriteLine("Interlocked Unaligned: {0} ms, read errors: {1}",
                interlockedUnalignedResult.elapsedMilliseconds,
                interlockedUnalignedResult.readErrors
            );

            Marshal.FreeHGlobal(allocatedMemory);
        }

        private static TestResult ReadWriteTest(int * value, ParameterizedThreadStart reader, ParameterizedThreadStart writer)
        {
            var readerThread = new Thread(reader);
            var writerThread = new Thread(writer);

            var timer = Stopwatch.StartNew();

            *value = 1;

            var parameter = new TaskParameters {
                value = value,
                stopSignal = false
            };
            
            writerThread.Start(parameter);
            readerThread.Start(parameter);

            writerThread.Join();
            readerThread.Join();

            return new TestResult(
                elapsedMilliseconds: timer.ElapsedMilliseconds,
                readErrors: parameter.readErrors
            );
        }

        private static void InterlockedWriterThread(object parameter)
        {
            var taskParameter = (TaskParameters) parameter;
            var pointer = taskParameter.value;
            
            for (var i = 0; i < NumberOfIterations; i += 1) {
                Interlocked.Exchange(ref *pointer, -1);
                Interlocked.Exchange(ref *pointer, 1);
            }

            taskParameter.stopSignal = true;
        }

        private static void InterlockedReaderThread(object parameter) 
        {
            var taskParameter = (TaskParameters) parameter;
            var pointer = taskParameter.value;
            var readErrors = 0;

            while (!taskParameter.stopSignal) {
                var value = Interlocked.CompareExchange(ref *pointer, 0, 0);

                if (value != 1 && value != -1) {
                    readErrors += 1;
                }
            }
            
            taskParameter.readErrors = readErrors;
        }
        
        private static void WriterThread(object parameter)
        {
            var taskParameter = (TaskParameters) parameter;
            var pointer = taskParameter.value;
            
            for (var i = 0; i < NumberOfIterations; i += 1) {
                *pointer = -1;
                *pointer = 1;
            }

            taskParameter.stopSignal = true;
        }

        private static void ReaderThread(object parameter) 
        {
            var taskParameter = (TaskParameters) parameter;
            var pointer = taskParameter.value;
            var readErrors = 0;

            while (!taskParameter.stopSignal) {
                var value = *pointer;

                if (value != 1 && value != -1) {
                    readErrors += 1;
                }
            }
            
            taskParameter.readErrors = readErrors;
        }
    }
}