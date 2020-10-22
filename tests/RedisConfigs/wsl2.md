If you're using WSL2, then the WSL2 instance is now a full VM with a separate IP address rather than being part of the current machine;
this means that if you're working on the *main desktop* with the server on the *VM*, the tests that expect it to work on the local machine will fail.
You *can* work around this with the `TestConfig.json` file, but you'd also need to disable `protected-mode` on the servers (we can't do that here,
because we use the same config files with the redis 3 on Windows tests, which does not allow that setting), *and* rebuild the redis cluster from scratch.

It is much easier to run the tests directly on the VM, honestly, where everything is local.