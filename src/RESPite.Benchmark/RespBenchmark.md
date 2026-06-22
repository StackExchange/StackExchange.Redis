# Influenced by redis-benchmark, which has typical output (with the default config) as below.

Keys used (by default):

- `key:__rand_int__`
- `counter:__rand_int__`
- `mylist`

====== PING_INLINE ======
100000 requests completed in 2.45 seconds
50 parallel clients
3 bytes payload
keep alive: 1

98.22% <= 1 milliseconds
99.88% <= 2 milliseconds
99.93% <= 3 milliseconds
99.99% <= 4 milliseconds
100.00% <= 5 milliseconds
100.00% <= 5 milliseconds
40849.68 requests per second

====== PING_BULK ======
100000 requests completed in 2.45 seconds
50 parallel clients
3 bytes payload
keep alive: 1

97.27% <= 1 milliseconds
99.86% <= 2 milliseconds
99.92% <= 3 milliseconds
99.94% <= 4 milliseconds
99.95% <= 23 milliseconds
99.96% <= 24 milliseconds
99.98% <= 25 milliseconds
100.00% <= 25 milliseconds
40866.37 requests per second

====== SET ======
100000 requests completed in 2.46 seconds
50 parallel clients
3 bytes payload
keep alive: 1

96.99% <= 1 milliseconds
99.47% <= 2 milliseconds
99.71% <= 3 milliseconds
99.86% <= 4 milliseconds
99.87% <= 9 milliseconds
99.88% <= 10 milliseconds
99.92% <= 11 milliseconds
99.93% <= 12 milliseconds
99.94% <= 13 milliseconds
99.96% <= 14 milliseconds
99.97% <= 15 milliseconds
99.97% <= 16 milliseconds
100.00% <= 27 milliseconds
40650.41 requests per second

====== GET ======
100000 requests completed in 3.00 seconds
50 parallel clients
3 bytes payload
keep alive: 1

90.56% <= 1 milliseconds
98.90% <= 2 milliseconds
99.46% <= 3 milliseconds
99.61% <= 4 milliseconds
99.70% <= 5 milliseconds
99.73% <= 6 milliseconds
99.75% <= 7 milliseconds
99.75% <= 9 milliseconds
99.77% <= 10 milliseconds
99.79% <= 12 milliseconds
99.80% <= 14 milliseconds
99.80% <= 15 milliseconds
99.83% <= 16 milliseconds
99.90% <= 17 milliseconds
99.93% <= 18 milliseconds
99.96% <= 19 milliseconds
99.98% <= 20 milliseconds
99.98% <= 22 milliseconds
99.98% <= 30 milliseconds
99.99% <= 31 milliseconds
100.00% <= 31 milliseconds
33377.84 requests per second

====== INCR ======
100000 requests completed in 2.94 seconds
50 parallel clients
3 bytes payload
keep alive: 1

93.21% <= 1 milliseconds
99.21% <= 2 milliseconds
99.70% <= 3 milliseconds
99.81% <= 4 milliseconds
99.86% <= 5 milliseconds
99.89% <= 6 milliseconds
99.93% <= 7 milliseconds
99.94% <= 8 milliseconds
99.96% <= 11 milliseconds
99.96% <= 12 milliseconds
99.96% <= 13 milliseconds
99.97% <= 14 milliseconds
99.97% <= 24 milliseconds
100.00% <= 24 milliseconds
34048.35 requests per second

====== LPUSH ======
100000 requests completed in 2.98 seconds
50 parallel clients
3 bytes payload
keep alive: 1

92.58% <= 1 milliseconds
99.21% <= 2 milliseconds
99.57% <= 3 milliseconds
99.71% <= 4 milliseconds
99.82% <= 5 milliseconds
99.85% <= 6 milliseconds
99.85% <= 7 milliseconds
99.88% <= 9 milliseconds
99.93% <= 10 milliseconds
99.93% <= 13 milliseconds
99.93% <= 14 milliseconds
99.95% <= 16 milliseconds
99.95% <= 31 milliseconds
99.99% <= 32 milliseconds
100.00% <= 32 milliseconds
33512.07 requests per second

====== LPOP ======
100000 requests completed in 2.91 seconds
50 parallel clients
3 bytes payload
keep alive: 1

92.81% <= 1 milliseconds
99.33% <= 2 milliseconds
99.89% <= 3 milliseconds
99.94% <= 4 milliseconds
99.96% <= 5 milliseconds
99.97% <= 15 milliseconds
99.98% <= 16 milliseconds
100.00% <= 17 milliseconds
34317.09 requests per second

====== SADD ======
100000 requests completed in 2.87 seconds
50 parallel clients
3 bytes payload
keep alive: 1

94.26% <= 1 milliseconds
99.58% <= 2 milliseconds
99.87% <= 3 milliseconds
99.93% <= 4 milliseconds
99.98% <= 17 milliseconds
99.98% <= 18 milliseconds
100.00% <= 19 milliseconds
34855.35 requests per second

====== SPOP ======
100000 requests completed in 2.99 seconds
50 parallel clients
3 bytes payload
keep alive: 1

91.00% <= 1 milliseconds
99.30% <= 2 milliseconds
99.69% <= 3 milliseconds
99.80% <= 4 milliseconds
99.85% <= 5 milliseconds
99.85% <= 8 milliseconds
99.86% <= 9 milliseconds
99.89% <= 10 milliseconds
99.92% <= 13 milliseconds
99.94% <= 14 milliseconds
99.95% <= 16 milliseconds
100.00% <= 16 milliseconds
33456.00 requests per second

====== LPUSH (needed to benchmark LRANGE) ======
100000 requests completed in 2.92 seconds
50 parallel clients
3 bytes payload
keep alive: 1

93.25% <= 1 milliseconds
99.45% <= 2 milliseconds
99.75% <= 3 milliseconds
99.86% <= 4 milliseconds
99.89% <= 5 milliseconds
99.91% <= 6 milliseconds
99.93% <= 9 milliseconds
99.95% <= 10 milliseconds
99.96% <= 11 milliseconds
99.97% <= 14 milliseconds
99.98% <= 15 milliseconds
99.99% <= 17 milliseconds
100.00% <= 18 milliseconds
100.00% <= 20 milliseconds
100.00% <= 20 milliseconds
34258.31 requests per second

====== LRANGE_100 (first 100 elements) ======
100000 requests completed in 4.33 seconds
50 parallel clients
3 bytes payload
keep alive: 1

35.50% <= 1 milliseconds
98.90% <= 2 milliseconds
99.61% <= 3 milliseconds
99.76% <= 4 milliseconds
99.83% <= 5 milliseconds
99.83% <= 7 milliseconds
99.84% <= 8 milliseconds
99.88% <= 9 milliseconds
99.88% <= 10 milliseconds
99.91% <= 11 milliseconds
99.91% <= 12 milliseconds
99.91% <= 13 milliseconds
99.96% <= 15 milliseconds
99.96% <= 34 milliseconds
99.97% <= 35 milliseconds
100.00% <= 39 milliseconds
100.00% <= 39 milliseconds
23089.36 requests per second

====== LRANGE_300 (first 300 elements) ======
100000 requests completed in 7.12 seconds
50 parallel clients
3 bytes payload
keep alive: 1

0.01% <= 1 milliseconds
84.00% <= 2 milliseconds
98.64% <= 3 milliseconds
99.44% <= 4 milliseconds
99.65% <= 5 milliseconds
99.70% <= 6 milliseconds
99.72% <= 7 milliseconds
99.75% <= 8 milliseconds
99.77% <= 9 milliseconds
99.81% <= 10 milliseconds
99.85% <= 11 milliseconds
99.87% <= 12 milliseconds
99.89% <= 13 milliseconds
99.90% <= 14 milliseconds
99.92% <= 15 milliseconds
99.96% <= 16 milliseconds
99.97% <= 17 milliseconds
99.99% <= 18 milliseconds
99.99% <= 26 milliseconds
99.99% <= 32 milliseconds
100.00% <= 37 milliseconds
100.00% <= 38 milliseconds
100.00% <= 39 milliseconds
14039.03 requests per second

====== LRANGE_500 (first 450 elements) ======
100000 requests completed in 8.32 seconds
50 parallel clients
3 bytes payload
keep alive: 1

0.71% <= 1 milliseconds
49.73% <= 2 milliseconds
96.81% <= 3 milliseconds
99.35% <= 4 milliseconds
99.79% <= 5 milliseconds
99.83% <= 6 milliseconds
99.84% <= 7 milliseconds
99.85% <= 8 milliseconds
99.91% <= 9 milliseconds
99.91% <= 10 milliseconds
99.91% <= 12 milliseconds
99.91% <= 27 milliseconds
99.91% <= 28 milliseconds
99.92% <= 29 milliseconds
99.93% <= 30 milliseconds
99.96% <= 31 milliseconds
99.96% <= 49 milliseconds
99.96% <= 50 milliseconds
99.98% <= 99 milliseconds
99.98% <= 100 milliseconds
100.00% <= 100 milliseconds
12022.12 requests per second

====== LRANGE_600 (first 600 elements) ======
100000 requests completed in 10.27 seconds
50 parallel clients
3 bytes payload
keep alive: 1

0.15% <= 1 milliseconds
28.15% <= 2 milliseconds
72.35% <= 3 milliseconds
96.20% <= 4 milliseconds
98.96% <= 5 milliseconds
99.68% <= 6 milliseconds
99.80% <= 7 milliseconds
99.85% <= 8 milliseconds
99.87% <= 9 milliseconds
99.88% <= 10 milliseconds
99.88% <= 11 milliseconds
99.88% <= 12 milliseconds
99.89% <= 13 milliseconds
99.89% <= 14 milliseconds
99.89% <= 15 milliseconds
99.90% <= 16 milliseconds
99.91% <= 17 milliseconds
99.91% <= 18 milliseconds
99.91% <= 19 milliseconds
99.92% <= 20 milliseconds
99.93% <= 21 milliseconds
99.95% <= 22 milliseconds
99.95% <= 23 milliseconds
99.96% <= 24 milliseconds
99.97% <= 25 milliseconds
99.97% <= 26 milliseconds
99.98% <= 27 milliseconds
100.00% <= 28 milliseconds
100.00% <= 29 milliseconds
100.00% <= 29 milliseconds
9736.15 requests per second

====== MSET (10 keys) ======
100000 requests completed in 2.94 seconds
50 parallel clients
3 bytes payload
keep alive: 1

92.48% <= 1 milliseconds
99.33% <= 2 milliseconds
99.91% <= 3 milliseconds
99.93% <= 4 milliseconds
99.94% <= 6 milliseconds
99.94% <= 11 milliseconds
99.96% <= 12 milliseconds
99.97% <= 13 milliseconds
99.98% <= 14 milliseconds
99.98% <= 17 milliseconds
99.99% <= 18 milliseconds
99.99% <= 19 milliseconds
99.99% <= 25 milliseconds
100.00% <= 30 milliseconds
100.00% <= 30 milliseconds
34059.95 requests per second