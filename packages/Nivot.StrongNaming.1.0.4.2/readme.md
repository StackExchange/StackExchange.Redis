Nivot.StrongNaming
==================

* v1.0.4.1 (something fishy is going on with my package build process?)
* v1.0.4 (fix missing mono.cecil)
* v1.0.3.2 (fix package: second attempt; readme.txt auto-open)
* v1.0.3.1 (fix package)
* v1.0.3  [2014/01/26]
  * Assembly references without a strongname will be given a  strong name using the same
    public key token as the primary target assembly.
* v1.0.2  [2013/04/30]
  * Added license and project URL.
  * Added readme.MD
* v1.0.1  [2013/04/29]
  * Updated metadata.
* v1.0.0  [2013/04/29]
  * Initial release.

Details
=======

All cmdlets accept pipeline input. The AssemblyFile parameter is aliased to PSPath, so it will
bind to piped files.  

* Test-StrongName [-AssemblyFile] <string[]>  [<CommonParameters>]

    Returns true if an assembly has a strong name.

* Import-StrongNameKeyPair [-KeyFile] <string>  [<CommonParameters>]
* Import-StrongNameKeyPair [-KeyFile] <string> -Password <securestring>  [<CommonParameters>]

    Imports a simple unprotected SNK or a password-protected PFX, returning a StrongNameKeyPair
 instance for consumption by Set-StrongName. If your PFX file has a blank password, you must
 provide a SecureString of the empty string "". SecureString instances are returned from
    the Read-Host cmdlet with the -AsSecureString parameter.

* Set-StrongName [-AssemblyFile] <string[]> -KeyPair <StrongNameKeyPair> [-NoBackup] [-Passthru]
    [-Force] [-DelaySign] [-WhatIf] [-Confirm]  [<CommonParameters>]

    Assigns a strong name identity to an assembly.

    The -KeyPair parameter accepts a System.Reflection.StrongNameKeyPair output from the
 Import-StrongNameKeyPair cmdlet., which accepts either simple unprotected SNK files or
 password-protected PFX files.

    The -NoBackup switch directs the cmdlet to skip creating a .bak file alongside the newly
 signed assembly. 

    The -Passthru switch will output a FileInfo representing the newly signed assembly to
 the pipeline.

    The -DelaySign switch will create a delay-signed assembly from a public key only SNK
 (it can also create one if the SNK contains both private and public keys.) This is useful
 if you can't get access to the full private key at your company. This will allow you to
 compile against previously unsigned nuget packages at least.

    The -Force switch will allow you to overwrite an existing strong name on an assembly.

    NOTE: You may supply -WhatIf to see what _would_ be done, without actually doing it.

*  Get-AssemblyName [-AssemblyFile] <string[]>  [<CommonParameters>]

    Returns a System.Reflection.AssemblyName instance from any assembly file.
    
FAQ: How Do I?
==============

# get the default package root folder
PM> $root = join-path (split-path $dte.solution.filename) packages

# load an unprotected snk 
PM> $key = Import-StrongNameKeyPair -KeyFile .\folder\key.snk
PM> dir *.dll | Set-StrongName -KeyPair $key -Verbose

# load a password-protected PFX
PM> $key = Import-StrongNameKeyPair -KeyFile .\folder\key.pfx -Password (Read-Host -AsSecureString)
******

# sign some unsigned assemblies
PM> cd (join-path $root unsignedPackage)
PM> dir -rec *.dll | set-strongname -keypair $key -verbose

# (re)sign some assemblies forcefully
PM> dir -rec *.dll | set-strongname -keypair $key -force

# sign only unsigned assemblies 
PM> dir -rec *.dll | where { -not (test-strongname $_) } | set-strongname -keypair $key -verbose