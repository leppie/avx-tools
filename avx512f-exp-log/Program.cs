using System.Diagnostics;
using System.Runtime.Intrinsics;

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
	Parallel.For(0, Environment.ProcessorCount,
		new ParallelOptions { CancellationToken = cts.Token },
		(x, s) =>
		{
			var v_r = Vector512.Create(14d + x * 4);
			var i = 0ul;

			while (!s.ShouldExitCurrentIteration)
			{
				v_r = Payload(v_r);
				i++;
			}

			Console.WriteLine($"{v_r} {i / sw.Elapsed.TotalMicroseconds:F3}");
		});
}
catch
{
}


static Vector512<double> Payload(Vector512<double> v_r)
{
	v_r = Vector512.Exp(v_r);
	v_r = Vector512.Log(v_r);
	v_r = Vector512.Exp(v_r);
	v_r = Vector512.Log(v_r);
	v_r = Vector512.Exp(v_r);
	v_r = Vector512.Log(v_r);
	v_r = Vector512.Exp(v_r);
	return Vector512.Log(v_r);
}