# terraria-mod (spike pending)
Framework decision made: TerrariaModder (net48, Harmony). See ../ARCHITECTURE.md "Still unverified".
Next file to add here: a minimal mod (manifest.json + ModBase subclass) that links the Protocol sources and runs
the transparency spike. The mod must compile against Terraria.exe / XNA 32-bit assemblies from your install,
so it is built on your Windows machine (reference paths come from TerrariaModder's setup.bat).
