CC ?= cc
CFLAGS ?= -std=c11 -O2 -Wall -Wextra -Wpedantic -Werror
LDLIBS ?= -pthread

.PHONY: all test clean

all: h2o

h2o: h2o.c
	$(CC) $(CFLAGS) $< -o $@ $(LDLIBS)

test: h2o
	./test.sh

clean:
	rm -f h2o
