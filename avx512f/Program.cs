using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using Microsoft.Win32.SafeHandles;
using System.Security;
using static Program.Op;

using Core = (ulong lc, ulong oc, int cpu, int tid, double timer, Program.Op[] ops);

class Program
{
	internal enum Op
	{
		Nop,
		Add,
		Sub,
		Mul,
		Div,
		Sqrt,
		Min,
		Max,
		Sum,
		Dot,
		FMA,
		Lerp,
		Copy,
		Write,
		Exp,
		Log,
		Log2,
		Cos,
		Sin,
		SinCos,
		Hypot,
		Sleep,
		Spin,
		Spin10,
		Yield
	}

	static void Main(string[] args)
	{
		if (!Vector512.IsHardwareAccelerated)
		{
			Console.WriteLine("AVX-512 is not supported on this system.");
			return;
		}

		var lcores = Environment.ProcessorCount;

		var all = new Core[lcores];

		if (args.Length == 0 && Console.IsInputRedirected)
		{
			var input = Console.In.ReadToEnd();
			var iops = new List<Op[]>();

			foreach (var line in input.Split(Environment.NewLine))
			{
				if (!string.IsNullOrWhiteSpace(line))
				{
					iops.Add(line.Split(" ", StringSplitOptions.RemoveEmptyEntries).Select(o => Enum.Parse<Op>(o, true)).ToArray());
				}
			}

			for (int i = 0; i < all.Length; i++)
			{
				all[i].ops = iops[i % iops.Count];
			}
		}

		var env_timeout = Environment.GetEnvironmentVariable("AVXTOOLS_TIMEOUT");

		if (!int.TryParse(env_timeout, out var timeout))
		{
			timeout = 60000;
		}

		CancellationTokenSource cts = new CancellationTokenSource();

		Console.CancelKeyPress += (s, e) => 
		{ 
			e.Cancel = true;
			cts.Cancel();
		};

		using var timer = new Timer((s) => 
		{
			var _all = all;
			_all = [.. all]; // probably need a lock around this

			var p = Process.GetCurrentProcess();
			p.Refresh();

			foreach (ProcessThread pt in p.Threads)
			{
				for (int i = 0; i < _all.Length; i++)
				{
					if (_all[i].tid == pt.Id)
					{
						_all[i].timer = pt.TotalProcessorTime.TotalMilliseconds;
						break;
					}
				}
			}

			PrintStats(_all); 
		});

		try
		{
			Op[] ops = Array.ConvertAll(args, a => Enum.Parse<Op>(a, true));
			timer.Change(1000, 1000);

			Parallel.For(0, all.Length, x =>
			{
				var tid = SetProcessorAffinity(1 << x);

				var p = Process.GetCurrentProcess();

				foreach (ProcessThread pt in p.Threads)
				{
					if (pt.Id == tid)
					{
						pt.IdealProcessor = x;
						break;
					}
				}

				Thread.BeginThreadAffinity();

				Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;

				all[x].cpu = x; // ???
				all[x].tid = tid;
				if (ops.Length > 0)
				{
					all[x].ops = ops;
				}

				cts.CancelAfter(timeout);
				RunPayload(ref all[x], cts.Token, all[x].ops);

				var cpu = Thread.GetCurrentProcessorId();
				all[x].cpu = cpu;

				p.Refresh();
				foreach (ProcessThread pt in p.Threads)
				{
					if (pt.Id == tid)
					{
						var ut = pt.TotalProcessorTime.TotalMilliseconds;
						all[x].timer = ut;
						break;
					}
				}

				Thread.EndThreadAffinity();
			});

			timer.Dispose();

			PrintStats(all, false);
		}
		catch
		{
			timer.Dispose();
			Console.WriteLine(string.Join(" ", Enum.GetNames<Op>()));
		}

		static void PrintStats(Core[] all, bool cursorReset = true)
		{
			var ct = Console.CursorTop;
			var bb = Console.BufferHeight;
			var firstTime = false;

			if (bb - ct < all.Length)
			{
				ct = bb - all.Length - 1;
				firstTime = true;
			}
			
			var sw = new StringWriter();

			var bl = all.MaxBy(x => x.oc / x.timer);

			Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;

			foreach (var a in all)
			{
				var isbl = (a == bl) && !firstTime;

				try
				{
					if (isbl)
					{
						var fc = Console.ForegroundColor;
						Console.ForegroundColor = Console.BackgroundColor;
						Console.BackgroundColor = fc;
					}

#if DEBUG
					Console.WriteLine($"[{a.cpu,2}] {(a.oc / a.timer) / (bl.oc / bl.timer),6:p0} {a.oc / (a.timer * 1000),6:f0} MOPS {1000/(a.oc / (a.timer * 1000)),8:F2} ns    {a}  {string.Join(" ", a.ops)}           ");
#else
					Console.WriteLine($"[{a.cpu,2}] {(a.oc / a.timer) / (bl.oc / bl.timer),6:p0} {a.oc / (a.timer * 1000),6:f0} MOPS {1000 / (a.oc / (a.timer * 1000)),8:F2} ns  {string.Join(" ", a.ops)}           ");
#endif
				}
				finally
				{
					if (isbl)
					{
						var fc = Console.ForegroundColor;
						Console.ForegroundColor = Console.BackgroundColor;
						Console.BackgroundColor = fc;
					}
				}
			}

			if (cursorReset)
			{
				Console.CursorTop = ct;
			}

			Thread.CurrentThread.Priority = ThreadPriority.Normal;
		}
	}

	readonly static Vector512<double> v_a_s = GetRandom(8);
	readonly static Vector512<double> v_b_s = GetRandom(8);

	static Vector512<double> GetRandom(int size)
	{
		var result = new double[size];
		for (int i = 0; i < size; i++)
		{
			result[i] = Random.Shared.NextDouble();
		}
		return Vector512.Create(result);
	}

	static void RunPayload(ref Core stats, CancellationToken cancellationToken, params Op[] ops)
	{
		ulong lc = 0, oc = 0; double timer = 0;
		double s = 100;
		double[] r = new double[8];

		var v_a = v_a_s;
		var v_b = v_b_s;
		var v_r = v_a;

		Vector512<double> expected = default;
		double expected_scalar = 0;

		var sw = Stopwatch.StartNew();

		while (true)
		{
			v_r = v_a;
			s = 100;

			for (int i = 0; i < ops.Length; i++)
			{
				switch (ops[i])
				{
					case Nop:
						break;
					case Add:
						v_r = Vector512.Add(v_r, v_b);
						break;
					case Sub:
						v_r = Vector512.Subtract(v_r, v_b);
						break;
					case Mul:
						v_r = Vector512.Multiply(v_r, v_b);
						break;
					case Div:
						v_r = Vector512.Divide(v_r, v_b);
						break;
					case Sqrt:
						v_r = Vector512.Sqrt(v_r);
						break;
					case Min:
						v_r = Vector512.Min(v_r, v_b);
						break;
					case Max:
						v_r = Vector512.Max(v_r, v_b);
						break;
					case Sum:
						s = Vector512.Sum(v_r);
						break;
					case Dot:
						s = Vector512.Dot(v_r, v_b);
						break;
					case FMA:
						v_r = Vector512.FusedMultiplyAdd(v_r, v_b, v_a);
						break;
					case Lerp:
						v_r = Vector512.Lerp(v_r, v_b, v_a);
						break;
					case Copy:
						v_r.TryCopyTo(r);
						break;
					case Write:
						v_r = v_a;
						break;
					case Exp:
						v_r = Vector512.Exp(v_r);
						break;
					case Log:
						v_r = Vector512.Log(v_r);
						break;
					case Log2:
						v_r = Vector512.Log2(v_r);
						break;
					case Cos:
						v_r = Vector512.Cos(v_r);
						break;
					case SinCos:
						(v_r,_) = Vector512.SinCos(v_r);
						break;
					case Sin:
						v_r = Vector512.Sin(v_r);
						break;
					case Hypot:
						v_r = Vector512.Hypot(v_r, v_b);
						break;
					case Sleep:
						Thread.Sleep(0);
						break;
					case Spin:
						Thread.SpinWait(1);
						break;
					case Spin10:
						Thread.SpinWait(10);
						break;
					case Yield:
						Thread.Yield();
						break;
				}
				oc++;
			}

			if ((lc >> 16) == 0 || (lc & 0xffff) == 0)
			{
#if !RESULT_CHECKING
				if (expected == default)
				{
					expected = v_r;
					expected_scalar = s;
				}
				else
				{
					if (v_r != expected || s != expected_scalar)
					{
						if (lc > 1)
						{
							Console.WriteLine($"Expectation failed ({lc}): {v_r} vs {expected}");
							throw new Exception();
						}

						expected = v_r;
						expected_scalar = s;
					}
				}
#endif
			}
			if ((lc & 0xffff) == 0)
			{
				timer = sw.ElapsedMilliseconds;
				stats.oc = oc;
				stats.lc = lc;
				stats.timer = timer;
				stats.cpu = Thread.GetCurrentProcessorId();

				if (cancellationToken.IsCancellationRequested)
				{
					break;
				}
			}

			lc++;
		}

		sw.Stop();
		timer = sw.Elapsed.TotalMilliseconds;

		stats.oc = oc;
		stats.lc = lc;
		stats.timer = timer;
	}

	static int SetProcessorAffinity(int coreMask)
	{
		int threadId = GetCurrentThreadId();
		SafeThreadHandle handle = null;
		var tempHandle = new object();
		try
		{
			handle = OpenThread(0x60, false, threadId);
			if (SetThreadAffinityMask(handle, new HandleRef(tempHandle, (IntPtr)coreMask)) == IntPtr.Zero)
			{
				throw new Exception("Failed to set processor affinity for thread");
			}
			return threadId;
		}
		finally
		{
			if (handle != null)
			{
				handle.Close();
			}
		}
	}

	[DllImport("kernel32", CharSet = CharSet.Auto, SetLastError = true)]
	static extern IntPtr SetThreadAffinityMask(SafeThreadHandle handle, HandleRef mask);

	[SuppressUnmanagedCodeSecurity]
	class SafeThreadHandle : SafeHandleZeroOrMinusOneIsInvalid
	{
		public SafeThreadHandle() : base(true)
		{

		}

		protected override bool ReleaseHandle()
		{
			return CloseHandle(handle);
		}
	}

	[DllImport("kernel32", CharSet = CharSet.Auto, SetLastError = true, ExactSpelling = true)]
	static extern bool CloseHandle(IntPtr handle);

	[DllImport("kernel32")]
	static extern int GetCurrentThreadId();

	[DllImport("kernel32", CharSet = CharSet.Auto, SetLastError = true)]
	static extern SafeThreadHandle OpenThread(int access, bool inherit, int threadId);
}