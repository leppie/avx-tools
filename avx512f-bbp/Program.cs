using System.Diagnostics;
using System.Runtime.Intrinsics;

//Console.WriteLine("Start");

var cts = new CancellationTokenSource();

Console.CancelKeyPress += (s, e) =>
{
	e.Cancel = true;
	cts.Cancel();
	//Console.WriteLine("Cancel");
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

	//Console.WriteLine("Before");

	Parallel.For(0, Environment.ProcessorCount,
		new ParallelOptions { CancellationToken = cts.Token },
		(x, s) =>
		{
			//Console.WriteLine("In " + x);
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
	Console.WriteLine(ex);
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


//var n = i * 8;
//var s = 
//	Vector512.Sum(
//		Vector512.Create(
//			Vector512.Sum((vt / (a.n[n + 0] + vb)) / a.e[n + 0]),
//			Vector512.Sum((vt / (a.n[n + 1] + vb)) / a.e[n + 1]),
//			Vector512.Sum((vt / (a.n[n + 2] + vb)) / a.e[n + 2]),
//			Vector512.Sum((vt / (a.n[n + 3] + vb)) / a.e[n + 3]),
//			Vector512.Sum((vt / (a.n[n + 4] + vb)) / a.e[n + 4]),
//			Vector512.Sum((vt / (a.n[n + 5] + vb)) / a.e[n + 5]),
//			Vector512.Sum((vt / (a.n[n + 6] + vb)) / a.e[n + 6]),
//			Vector512.Sum((vt / (a.n[n + 7] + vb)) / a.e[n + 7])));



/*
static Vector512<double> Payload(Vector512<double> v_r)
{
	Thread.BeginThreadAffinity();

	double[] t = [4, -2, -1, -1];
	double[] b = [1, 4, 5, 6];

	var vht = Vector256.Create(t);
	var vhb = Vector256.Create(b);

	var vt = Vector512.Create(vht, vht);
	var vb = Vector512.Create(vhb, vhb);

	var f = 0.0;

	for (int n = 0; n < 256; n += 2)
	{
		var v = Vector512.Create(
					Vector256.Create(8.0 * n),
					Vector256.Create(8.0 * (n + 1)));

		v = v + vb;
		v = vt / v;

		var e = Math.Pow(16, n);
		var ve = Vector512.Create(
					Vector256.Create(e),
					Vector256.Create(e * 16));

		v = v / ve;

		var s = Vector512.Sum(v);

		f = f + s;
	}

	if (f != Math.PI) throw new Exception($"Fail: Not π! {f} CPU: {Thread.GetCurrentProcessorId()}");

	Thread.EndThreadAffinity();

	return v_r * (f / Math.PI);
}
*/