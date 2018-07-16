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
            
            // Allocated memory is aligned on 8 bytes, so if we add CacheLineSize bytes to the base address (let's say
            // the address in the 1st cache line) we'll get the address in the 2nd cache line. Then we divide it
            // by CacheLineSize to get the theoretical cache line number. We are using integer division so the number
            // will be an integer. After we are multiplying the address by CacheLineSize to get the beginning of
            // the 2nd cache line.
            var cacheLineStart = ((long) allocatedMemory + CacheLineSize) / CacheLineSize * CacheLineSize;
            
            Console.WriteLine("Running benchmark...");
            
            var alignedResult = ReadWriteTest((int*) cacheLineStart,
                ReaderThread,
                WriterThread
            );
            
            var unalignedResult = ReadWriteTest((int*) (cacheLineStart + Offset),
                ReaderThread,
                WriterThread
            );

            var interlockedAlignedResult = ReadWriteTest((int*) cacheLineStart,
                InterlockedReaderThread,
                InterlockedWriterThread
            );
            
            var interlockedUnalignedResult = ReadWriteTest((int*) (cacheLineStart + Offset),
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