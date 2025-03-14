using System.Diagnostics;
using System.Runtime.Intrinsics;

#pragma warning disable CA1416 // Validate platform compatibility

var pc = Environment.ProcessorCount;

if (args.Length == 1)
{
	var na = Convert.ToInt32(args[0], 16);

	var process = Process.GetCurrentProcess();

	var pm = (int)process.ProcessorAffinity;

	if ( na != pm)
	{
		process.ProcessorAffinity = na;
		pm = (int)process.ProcessorAffinity;
	}

	var mask = pm;
	var bc = 0;

	while (mask != 0)
	{
		bc += mask & 1;
		mask >>= 1;
	}

	pc = bc;
}

var cts = new CancellationTokenSource();

Console.CancelKeyPress += (s, e) =>
{
	e.Cancel = true;
	cts.Cancel();
};

var env_timeout = Environment.GetEnvironmentVariable("AVXTOOLS_TIMEOUT");

if (int.TryParse(env_timeout, out var timeout))
{
	cts.CancelAfter(timeout);
}

var sw = Stopwatch.StartNew();

try
{
	var a = Init();

	Parallel.For(0, pc,
		new ParallelOptions { CancellationToken = cts.Token },
		(x, s) =>
		{
			Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
			var v_r = Vector512.Create(14d + x * 4);
			var i = 0ul;

			while (!s.ShouldExitCurrentIteration)
			{
				Payload(ref a);
				i++;
			}

			Console.WriteLine($"{v_r} {i / sw.Elapsed.TotalMicroseconds:F3}");
		});
}
catch (OperationCanceledException)
{
}
catch (Exception ex)
{
	Console.Error.WriteLine(ex);
	Environment.ExitCode = 1;
}

static (Vector512<double>, Vector512<double>, Vector512<double>[], Vector512<double>[]) Init()
{
	double[] t = [4, -2, -1, -1];
	double[] b = [1, 4, 5, 6];

	var vht = Vector256.Create(t);
	var vhb = Vector256.Create(b);

	var vt = Vector512.Create(vht, vht);
	var vb = Vector512.Create(vhb, vhb);

	var n = new Vector512<double>[128];
	var e = new Vector512<double>[128];

	for (int i = 0; i < 128; i++)
	{
		var v = Vector512.Create(
					Vector256.Create(8.0 * (i * 2)),
					Vector256.Create(8.0 * (i * 2 + 1)));

		n[i] = v;

		var p = Math.Pow(16d, i * 2);

		var ve = Vector512.Create(
					Vector256.Create(p),
					Vector256.Create(p * 16));

		e[i] = ve;
	}

	return (vt, vb, n, e);
}

static void Payload(ref (Vector512<double> vt, Vector512<double> vb, Vector512<double>[] n, Vector512<double>[] e) a)
{
	var f = 0.0;

	for (int n = 0; n < 128; n++)
	{
		var s = Vector512.Sum(a.vt / ((a.n[n] + a.vb) * a.e[n]));
		f = f + s;
	}

	if (f != Math.PI) throw new Exception($"Fail: Not π! {f} CPU: {Thread.GetCurrentProcessorId()}");
}
