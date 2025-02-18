# avx-tools

Simple tool to create an artifical payload of AVX512 operations.


## avx512f

AVX-512 floating point ops.

### Usage

```
avx512f op [op ...]
```

The will run the same ops sequence on every logical CPU.

or an 'ops' file as

```
avx512f < ops
```

The ops file is used as follows:

1. Each non-blank line is parsed as ops
2. The parsed set is then applied sequentially to all logical CPU

Example ops file:

```
sin
add
mul
```

On an 8 CPU system it will be distrubuted as follows:

```
[0] sin
[1] add
[2] mul
[3] sin
[4] add
[5] mul
[6] sin
[7] add
```

The payload will run for 60 seconds. If you want a different time, set the `AVXTOOLS_TIMEOUT` env var to the desired number of milliseconds the payload should run. You can also quit by pressing Ctrl+C.

### Ops 

- Nop 
- Add 
- Sub 
- Mul 
- Div 
- Sqrt 
- Min 
- Max 
- Sum 
- Dot 
- FMA 
- Lerp 
- Copy 
- Write 
- Exp 
- Log 
- Log2 
- Cos 
- Sin 
- SinCos 
- Hypot 
- Sleep 
- Spin 
- Spin10 
- Yield