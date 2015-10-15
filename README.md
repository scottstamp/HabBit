# HabBit
This application was designed to modify the .abc blocks in Habbo's game client to allow it to run anywhere, with custom handshake parameters.
Powered by [FlashInspect](https://github.com/ArachisH/FlashInspect)(AS3 Shockwave Flash(SWF) file (dis)assembler.)

[Latest Release(s)](https://github.com/ArachisH/HabBit/releases)
## Requirements
* .NET Framework 2.0

## Requirements(Source)
* IDE with C# 6 support.

## Capabilties
* Public RSA key pair replacement.
* Modifes the "isValidHabboDomain" method to always return true.
* Alters method that would unload the client if the client was running on an unknown host.
* Disable RC4 encr/decry methods by having them return the same `ByteArray` input when invoked.

## Commands/Arguments
* `n:X` - Will be used as the modulus value when replacing the RSA keys.
* `e:X` - Will be used as the exponent value when replacing the RSA keys.
* `disablerc4` - Modifes the RC4 methods so that the client won't encrypt/decrypt any data.
* `skipcompress` - Will not compress the reconstructed  client, disabling this will increase the file size, but will also also be quicker to complete.

Default RSA Keys
```
E:3
N:86851dd364d5c5cece3c883171cc6ddc5760779b992482bd1e20dd296888df91b33b936a7b93f06d29e8870f703a216257dec7c81de0058fea4cc5116f75e6efc4e9113513e45357dc3fd43d4efab5963ef178b78bd61e81a14c603b24c8bcce0a12230b320045498edc29282ff0603bc7b7dae8fc1b05b52b2f301a9dc783b7
```

## Console Interface
![HabBit](http://i.imgur.com/eaDVja6.png)
