using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Rewriter
{
	class Program
	{
		static void Main (string[] args)
		{
			if (args.Length != 2) {
				Console.WriteLine ("Usage: {0} [source] [target]",
					Path.GetFileName (typeof (Program).Assembly.ManifestModule.FullyQualifiedName));
				Environment.Exit (-1);
			}

			var sourceFile = new FileInfo(args[0]).FullName;
			var targetFile = new FileInfo(args[1]).FullName;

			var asm = AssemblyDefinition.ReadAssembly(sourceFile);
			var vbApp = asm.MainModule.GetType ("Microsoft.SmallBasic.Library.Internal.SmallBasicApplication");

			ClearEnd (vbApp);

			var cctor = vbApp.Resolve ().Methods.FirstOrDefault (m => m.IsConstructor && m.IsStatic);
			var il = cctor.Body.GetILProcessor ();
			foreach (var i in cctor.Body.Instructions.ToArray ()) {
				il.Remove (i);
			}

			var wpf = ModuleDefinition.ReadModule(typeof(Application).Assembly.ManifestModule.FullyQualifiedName);
			var winbase = ModuleDefinition.ReadModule(typeof(Dispatcher).Assembly.ManifestModule.FullyQualifiedName);
			var mscorlib = ModuleDefinition.ReadModule(typeof(Thread).Assembly.ManifestModule.FullyQualifiedName);

			asm.MainModule.Import (typeof (Application));
			asm.MainModule.Import (typeof (Dispatcher));
			asm.MainModule.Import (typeof (DispatcherObject));
			asm.MainModule.Import (typeof (Thread));

			var appType = asm.MainModule.Import(typeof(Application));
			var dispatcherType = asm.MainModule.Import(typeof(Dispatcher));
			var threadType = asm.MainModule.Import(typeof(Thread));

			var _application = asm.MainModule.Import(new FieldReference("_application", appType, vbApp));
			var _dispatcher = asm.MainModule.Import(new FieldReference("_dispatcher", dispatcherType, vbApp));
			var _applicationThread = asm.MainModule.Import(new FieldReference("_applicationThread", threadType, vbApp));
			var appCurrent = asm.MainModule.Import(typeof(Application).GetProperty("Current").GetGetMethod());

			// _application = Application.Current;
			il.Append (Instruction.Create (OpCodes.Call, appCurrent));
			il.Append (Instruction.Create (OpCodes.Stsfld, _application));

			var getDispatcher = asm.MainModule.Import(typeof(DispatcherObject).GetProperty("Dispatcher").GetGetMethod());

			// _dispatcher = _application.Dispatcher;
			il.Append (Instruction.Create (OpCodes.Ldsfld, _application));
			il.Append (Instruction.Create (OpCodes.Callvirt, getDispatcher));
			il.Append (Instruction.Create (OpCodes.Stsfld, _dispatcher));

			var getThread = asm.MainModule.Import(typeof(Dispatcher).GetProperty("Thread").GetGetMethod());

			// _applicationThread = _dispatcher.Thread;
			il.Append (Instruction.Create (OpCodes.Ldsfld, _dispatcher));
			il.Append (Instruction.Create (OpCodes.Callvirt, getThread));
			il.Append (Instruction.Create (OpCodes.Stsfld, _applicationThread));

			il.Append (Instruction.Create (OpCodes.Ret));

			var targetDir = Path.GetDirectoryName(targetFile);
			if (!Directory.Exists (targetDir))
				Directory.CreateDirectory (targetDir);

			asm.Write (targetFile);

			var verified = Verify (targetFile);

			Environment.Exit (verified);
		}

		private static int Verify (string targetFile)
		{
			// Locate PEVerify
			//HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Microsoft SDKs\NETFXSDK\4.6.1\WinSDK - NetFx40Tools - x86
			using (var key = Registry.LocalMachine.OpenSubKey (@"SOFTWARE\Microsoft\Microsoft SDKs\NETFXSDK", false)) {
				if (key != null && key.SubKeyCount >= 0) {
					// 4.6 / 4.6.1, etc.
					using (var versionKey = key.OpenSubKey (key.GetSubKeyNames ()[0])) {
						// WinSDK-NetFx40Tools
						using (var toolsKey = versionKey.OpenSubKey (versionKey.GetSubKeyNames ()[0])) {
							var installPath = (string)toolsKey.GetValue("InstallationFolder");

							var process = Process.Start (new ProcessStartInfo(Path.Combine(installPath, "PEVerify.exe"), targetFile) {
								RedirectStandardOutput = true,
								UseShellExecute = false, 
								CreateNoWindow = true, 
								WindowStyle = ProcessWindowStyle.Hidden
							});

							Console.WriteLine (process.StandardOutput.ReadToEnd ());
							process.WaitForExit ();
							return process.ExitCode;
						}
					}
				} else {
					Console.WriteLine ();
					return -1;
				}
			}
		}

		private static void ClearEnd (TypeDefinition vbApp)
		{
			var end = vbApp.Resolve ().Methods.First (m => m. Name == "End");
			var il = end.Body.GetILProcessor();

			foreach (var i in end.Body.Instructions.ToArray ()) {
				il.Remove (i);
			}

			il.Append (il.Create (OpCodes.Ret));
		}
	}
}
