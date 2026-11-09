namespace FluentBlock.Models;

public sealed record LockHolder(
    string ProcessName,   // "notepad.exe"
    int Pid,
    string User,          // @"TITAN\Principale" or ""
    string HandleHex,    // "0x00000294"
    string FilePath,     // DOS path "E:\docs\\file.txt"
    string Access);       // "0x0012019F" granted access mask ("" when unknown)
