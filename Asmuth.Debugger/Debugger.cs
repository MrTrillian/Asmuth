﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Asmuth.Debugger
{
	using static Advapi32;
	using static NativeMethods;
	using static Kernel32;
	using System.Runtime.InteropServices;
	public sealed partial class Debugger : IDisposable
	{
		private readonly ConcurrentDictionary<int, ProcessAttachment> processAttachments
			= new ConcurrentDictionary<int, ProcessAttachment>();
		private readonly ConcurrentQueue<Action> workerThreadRequests = new ConcurrentQueue<Action>();
		private readonly Thread workerThread;
		private volatile bool disposing;

		public Debugger()
		{
			workerThread = new Thread((ParameterizedThreadStart)WorkerThreadMain);
			workerThread.Name = "Debugger message pump thread";
			workerThread.Start(this);
		}

		public Task<ProcessDebugger> AttachToProcessAsync(int processID, bool @break)
		{
			if (disposing) throw new InvalidOperationException();

			var processAttachment = new ProcessAttachment(initialBreak: @break);
			if (!processAttachments.TryAdd(processID, processAttachment))
				throw new InvalidOperationException();
			
			EnqueueWorkerThreadRequest(() =>
			{
				if (!DebugActiveProcess(unchecked((uint)processID)))
				{
					ProcessAttachment removedProcessAttachment;
					processAttachments.TryRemove(processID, out removedProcessAttachment);
					Contract.Assert(removedProcessAttachment == processAttachment);
					processAttachment.Fail(GetLastWin32Exception());
				}
			});
			
			return processAttachment.Task;
		}

		public Task<ProcessDebugger> AttachToProcessAsync(Process process, bool @break)
			=> AttachToProcessAsync(process.Id, @break);

		public void Dispose()
		{
			disposing = true;
			workerThread.Join();

			foreach (var processAttachment in processAttachments.Values)
				processAttachment.Dispose();
			processAttachments.Clear();
		}

		internal void EnqueueWorkerThreadRequest(Action action)
		{
			workerThreadRequests.Enqueue(action);
		}

		private void ProcessEvent(ref DEBUG_EVENT debugEvent)
		{
			ProcessAttachment processAttachment;
			if (!processAttachments.TryGetValue(unchecked((int)debugEvent.dwProcessId), out processAttachment))
			{
				// Unexpected, we should have been asked to attach to the process
				throw new InvalidOperationException();
			}

			// If we've just attached to a new process, complete the attach event.
			ProcessDebugger process;
			if (debugEvent.dwDebugEventCode == CREATE_PROCESS_DEBUG_EVENT)
			{
				process = new ProcessDebugger(this, debugEvent.CreateProcessInfo);

				// Resume before completing the task so the client
				// doesn't race calling process.BreakAsync
				if (!processAttachment.InitialBreak)
					CheckWin32(ContinueDebugEvent(debugEvent.dwProcessId, debugEvent.dwThreadId, DBG_CONTINUE));

				processAttachment.Complete(process);
				return;
			}
			else
			{
				process = processAttachment.Process;
			}

			bool @break = false;
			switch (debugEvent.dwDebugEventCode)
			{
				case EXCEPTION_DEBUG_EVENT:
					{
						var record = IntPtr.Size == sizeof(int)
							? ExceptionRecord.FromStruct(ref debugEvent.Exception32.ExceptionRecord)
							: ExceptionRecord.FromStruct(ref debugEvent.Exception64.ExceptionRecord);
						process.OnException(debugEvent.dwThreadId, record, @break: out @break);
					}
					break;

				case CREATE_THREAD_DEBUG_EVENT:
					process.OnThreadCreated(unchecked((int)debugEvent.dwThreadId), debugEvent.CreateThread, @break: out @break);
					break;

				case EXIT_THREAD_DEBUG_EVENT:
					process.OnThreadExited(debugEvent.dwThreadId, debugEvent.ExitThread.dwExitCode, @break: out @break);
					break;

				case LOAD_DLL_DEBUG_EVENT:
					process.OnModuleLoaded(debugEvent.LoadDll, @break: out @break);
					break;

				case OUTPUT_DEBUG_STRING_EVENT:
					{
						var str = debugEvent.DebugString.fUnicode == 0
							? Marshal.PtrToStringAnsi(debugEvent.DebugString.lpDebugStringData, debugEvent.DebugString.nDebugStringLength)
							: Marshal.PtrToStringUni(debugEvent.DebugString.lpDebugStringData, debugEvent.DebugString.nDebugStringLength);
						process.OnOutputString(str, @break: out @break);
					}
					break;

				case UNLOAD_DLL_DEBUG_EVENT:
					process.OnModuleUnloaded((ulong)debugEvent.UnloadDll.lpBaseOfDll, @break: out @break);
					break;

				default:
					Debug.Fail("Unknown debug code.");
					break;
			}

			if (!@break)
			{
				CheckWin32(ContinueDebugEvent(debugEvent.dwProcessId, debugEvent.dwThreadId, DBG_CONTINUE));
			}
		}

		private void WorkerThreadMain()
		{
			AcquireDebuggingPrivilege();

			while (!disposing)
			{
				Action request;
				while (workerThreadRequests.TryDequeue(out request))
					request();

				DEBUG_EVENT debugEvent;
				if (WaitForDebugEventEx(out debugEvent, 50))
					ProcessEvent(ref debugEvent);
			}
		}

		private static void WorkerThreadMain(object instance)
		{
			((Debugger)instance).WorkerThreadMain();
		}

		private static void AcquireDebuggingPrivilege()
		{
			IntPtr tokenHandle;
			CheckWin32(OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out tokenHandle));
			try
			{
				LUID valueLuid;
				CheckWin32(LookupPrivilegeValue(null, SE_DEBUG_NAME, out valueLuid));

				TOKEN_PRIVILEGES tokenPriviledges;
				tokenPriviledges.PrivilegeCount = 1;
				tokenPriviledges.Privilege.Luid = valueLuid;
				tokenPriviledges.Privilege.Attributes = SE_PRIVILEGE_ENABLED;
				CheckWin32(AdjustTokenPrivileges(tokenHandle, false, ref tokenPriviledges, 0));
			}
			finally
			{
				CloseHandle(tokenHandle);
			}
		}
	}
}
