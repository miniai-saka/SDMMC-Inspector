// sdmmc_inspect.cpp -- Windows x64, read-only SD/MMC inventory and metadata
// Native Win32 ABI declarations (no MSVC CRT or WDK required).
// Target OS: Windows 10/11 x64. SD pass-through is opt-in, READ commands only.
// Use with administrator privileges if access is denied.
using U8 = unsigned char;
using U16 = unsigned short;
using U32 = unsigned int;
using U64 = unsigned long long;
using I32 = int;
using HANDLE = void*;
using BOOL = int;

#ifndef SDMMC_EMBEDDED
// The standalone CLI has no CRT. An embedded build uses the host runtime's implementations.
extern "C" void* __cdecl memcpy(void*, const void*, decltype(sizeof(0)));
extern "C" void* __cdecl memset(void*, int, decltype(sizeof(0)));
#if defined(_MSC_VER) && !defined(__clang__)
#pragma function(memcpy, memset)
#endif
extern "C" void* __cdecl memcpy(void* destination, const void* source, decltype(sizeof(0)) size) {
    auto* bytes=static_cast<volatile U8*>(destination);
    const auto* input=static_cast<const U8*>(source);
    for (decltype(size) i=0; i<size; i++) bytes[i]=input[i];
    return destination;
}
extern "C" void* __cdecl memset(void* destination, int value, decltype(sizeof(0)) size) {
    auto* bytes=static_cast<volatile U8*>(destination);
    for (decltype(size) i=0; i<size; i++) bytes[i]=U8(value);
    return destination;
}
#endif

extern "C" {
__declspec(dllimport) const wchar_t* __stdcall GetCommandLineW();
__declspec(dllimport) wchar_t** __stdcall CommandLineToArgvW(const wchar_t*, int*);
__declspec(dllimport) void* __stdcall LocalFree(void*);
__declspec(dllimport) HANDLE __stdcall CreateFileW(const wchar_t*, U32, U32, void*, U32, U32, HANDLE);
__declspec(dllimport) BOOL __stdcall DeviceIoControl(HANDLE, U32, void*, U32, void*, U32, U32*, void*);
__declspec(dllimport) U32 __stdcall GetLastError();
__declspec(dllimport) BOOL __stdcall CloseHandle(HANDLE);
__declspec(dllimport) HANDLE __stdcall GetStdHandle(U32);
__declspec(dllimport) BOOL __stdcall GetConsoleMode(HANDLE, U32*);
__declspec(dllimport) BOOL __stdcall ReadConsoleW(HANDLE, void*, U32, U32*, void*);
__declspec(dllimport) BOOL __stdcall ReadFile(HANDLE, void*, U32, U32*, void*);
__declspec(dllimport) int __stdcall MultiByteToWideChar(U32, U32, const char*, int, wchar_t*, int);
__declspec(dllimport) BOOL __stdcall WriteFile(HANDLE, const void*, U32, U32*, void*);
__declspec(dllimport) U32 __stdcall GetLogicalDrives();
__declspec(dllimport) U32 __stdcall GetDriveTypeW(const wchar_t*);
__declspec(dllimport) __declspec(noreturn) void __stdcall ExitProcess(U32);
}

constexpr U32 READ_ = 0x80000000u;
constexpr U32 WRITE_ = 0x40000000u;
constexpr U32 SHARE_READ_ = 1;
constexpr U32 SHARE_WRITE_ = 2;
constexpr U32 SHARE_DELETE_ = 4;
constexpr U32 OPEN_EXISTING_ = 3;
constexpr U32 CREATE_ALWAYS_ = 2;
constexpr U32 CREATE_NEW_ = 1;
constexpr U32 ATTR_NORMAL_ = 0x80;
constexpr U32 IOCTL_DISK_GET_LENGTH_INFO_ = 0x0007405Cu;
constexpr U32 IOCTL_STORAGE_QUERY_PROPERTY_ = 0x002D1400u;
constexpr U32 IOCTL_STORAGE_GET_DEVICE_NUMBER_ = 0x002D1080u;
constexpr U32 IOCTL_SFFDISK_QUERY_DEVICE_PROTOCOL_ = 0x00071E80u;
constexpr U32 IOCTL_SFFDISK_MMC_QUERY_EXT_CSD_ = 0x00071E94u;
constexpr U32 IOCTL_SFFDISK_DEVICE_COMMAND_ = 0x00079E84u;
static_assert((7u << 16 | (0x7A5u << 2)) == IOCTL_SFFDISK_MMC_QUERY_EXT_CSD_, "EXT_CSD IOCTL invalid");
static_assert((7u << 16 | (2u << 14) | (0x7A1u << 2)) == IOCTL_SFFDISK_DEVICE_COMMAND_, "Device command IOCTL invalid");

struct Guid { U32 a; U16 b; U16 c; U8 d[8]; };
struct SffProtocol { U16 Size; U16 Reserved; Guid ProtocolGUID; };
struct StorageDeviceNumber { U32 DeviceType; U32 DeviceNumber; U32 PartitionNumber; };
struct StorageQuery { U32 PropertyId; U32 QueryType; U8 AdditionalParameters[1]; };
struct StorageDescriptor {
    U32 Version; U32 Size;
    U8 DeviceType; U8 DeviceTypeModifier; U8 RemovableMedia; U8 CommandQueueing;
    U32 VendorIdOffset; U32 ProductIdOffset; U32 ProductRevisionOffset;
    U32 SerialNumberOffset; U32 BusType; U32 RawPropertiesLength;
    U8 RawDeviceProperties[1];
};
// Windows x64 SFFDISK_DEVICE_COMMAND_DATA, followed by a 20-byte SDCMD_DESCRIPTOR
struct SffDeviceCommandData {
    U16 HeaderSize; U16 Flags; U32 Command; U16 ProtocolArgumentSize;
    U32 DeviceDataBufferSize; U64 Information;
};
struct SdCmdDescriptor {
    U32 Cmd, CmdClass, TransferDirection, TransferType, ResponseType;
};
static_assert(sizeof(Guid) == 16, "GUID ABI mismatch");
static_assert(sizeof(SffProtocol) == 20, "SFF protocol ABI mismatch");
static_assert(sizeof(StorageDeviceNumber) == 12, "Device number ABI mismatch");
static_assert(sizeof(SffDeviceCommandData) == 24, "x64 SFF command ABI mismatch");
static_assert(sizeof(SdCmdDescriptor) == 20, "SD descriptor ABI mismatch");
static_assert(sizeof(StorageDescriptor) == 40, "Storage descriptor ABI mismatch");

const Guid MMC_GUID = {0x77274D3Fu,0x2365,0x4491,{0xA0,0x30,0x8B,0xB4,0x4A,0xE6,0x00,0x97}};
const Guid SD_GUID  = {0xAD7536A8u,0xD055,0x4C40,{0xAA,0x4D,0x96,0x31,0x2D,0xDB,0x6B,0x38}};
enum Proto { PROTO_UNKNOWN=0, PROTO_SD=1, PROTO_MMC=2 };

void out(const char* p) {
    if(!p) return;
    U32 n=0; while(n < 8192 && p[n]) ++n;
    U32 written=0;
    if(n) WriteFile(GetStdHandle(0xFFFFFFF5u), p, n, &written, nullptr); // STD_OUTPUT_HANDLE = -11
}
void nl() { out("\r\n"); }
void decimal(U64 n) {
    char s[32]; int p=31; s[p]=0;
    do { s[--p] = char('0'+(n%10)); n/=10; } while(n);
    out(s+p);
}
void hexadecimal(U64 n,int w=2) {
    char s[18]; if(w<1||w>16) return;
    for(int i=0;i<w;i++) { int d=int((n>>(4*(w-i-1)))&15u); s[i]=char(d<10?'0'+d:'A'+d-10); }
    s[w]=0; out(s);
}
void error(const char* label, U32 e) {
    out(label); out(": Win32 error="); decimal(e); out(" (0x"); hexadecimal(e,8); out(")\r\n");
}
bool equalGuid(const Guid& x,const Guid& y) {
    if(x.a!=y.a || x.b!=y.b || x.c!=y.c) return false;
    for(int i=0;i<8;i++) if(x.d[i]!=y.d[i]) return false;
    return true;
}
bool same(const wchar_t* a,const wchar_t* b) {
    if(!a||!b) return false;
    for(;*a&&*b;a++,b++) if(*a!=*b) return false;
    return *a==*b;
}
bool parseNumber(const wchar_t* p,U32& value) {
    if(!p||!*p) return false;
    U32 v=0;
    while(*p) { if(*p<L'0'||*p>L'9') return false;
        U32 d=U32(*p-L'0'); if(v>(0xFFFFFFFFu-d)/10u) return false; v=v*10u+d; ++p; }
    value=v; return true;
}
bool parseDisk(const wchar_t* text, U32& disk) {
    return parseNumber(text, disk) && disk <= 4095;
}
bool parseVolume(const wchar_t* text, wchar_t& volume) {
    wchar_t letter = text[0];
    if (letter >= L'a' && letter <= L'z') letter -= L'a' - L'A';
    if (letter < L'A' || letter > L'Z') return false;
    if (text[1] && (text[1] != L':' || text[2])) return false;
    volume = letter;
    return true;
}
void zero(U8* p,U32 size) { for(U32 i=0;i<size;i++) p[i]=0; }
void copy(U8* d,const U8* s,U32 n) { for(U32 i=0;i<n;i++) d[i]=s[i]; }
U32 le32(const U8* p) { return U32(p[0]) | (U32(p[1])<<8) | (U32(p[2])<<16) | (U32(p[3])<<24); }
void diskPath(U32 num,wchar_t path[48]) {
    const wchar_t head[]={L'\\',L'\\',L'.',L'\\',L'P',L'h',L'y',L's',L'i',L'c',L'a',L'l',L'D',L'r',L'i',L'v',L'e'};
    int k=0; for(int i=0;i<17;i++) path[k++]=head[i];
    wchar_t rev[12]; int j=0;
    do { rev[j++]=wchar_t(L'0'+(num%10)); num/=10; } while(num && j<11);
    while(j) path[k++]=rev[--j]; path[k]=0;
}
void volumePath(wchar_t letter,wchar_t path[48]) {
    path[0]=L'\\';path[1]=L'\\';path[2]=L'.';path[3]=L'\\';
    path[4]=letter;path[5]=L':';path[6]=0;
}
HANDLE openDevice(const wchar_t* path, U32 access = READ_) {
    return CreateFileW(path,access,
        SHARE_READ_|SHARE_WRITE_|SHARE_DELETE_,nullptr,OPEN_EXISTING_,ATTR_NORMAL_,nullptr);
}
bool valid(HANDLE h) { return h!=reinterpret_cast<HANDLE>(-1); }

const char* busName(U32 type) {
    switch(type) {
    case 1:return "SCSI"; case 3:return "ATA"; case 7:return "USB";
    case 8:return "RAID"; case 11:return "SATA"; case 12:return "SD";
    case 13:return "MMC"; case 14:return "Virtual"; case 17:return "NVMe";
    case 19:return "UFS"; default:return "Other/Unknown";
    }
}
struct StorageInfo {
    U32 bus;
    char fields[4][128];
};
const char* const storageNames[] = {"Vendor", "Product", "Revision", "Serial"};
const char* const storageKeys[] = {"vendor", "product", "revision", "serial"};

bool readStorage(HANDLE h, StorageInfo& info) {
    StorageQuery q={}; q.PropertyId=0; q.QueryType=0;
    alignas(StorageDescriptor) U8 result[1024]; zero(result,sizeof(result)); U32 got=0;
    BOOL ok=DeviceIoControl(h,IOCTL_STORAGE_QUERY_PROPERTY_,&q,sizeof(q),result,sizeof(result),&got,nullptr);
    if(!ok || got<36 || got>sizeof(result)) return false;
    const StorageDescriptor* d=reinterpret_cast<const StorageDescriptor*>(result);
    info.bus=d->BusType;
    const U32 offsets[4]={d->VendorIdOffset,d->ProductIdOffset,d->ProductRevisionOffset,d->SerialNumberOffset};
    for(int i=0;i<4;i++) {
        U32 offset=offsets[i]; if(offset==0 || offset>=got) continue;
        U32 j=0;
        while(offset+j<got && j<sizeof(info.fields[i])-1) {
            U8 c=result[offset+j]; if(c==0) break;
            info.fields[i][j++]=char((c>=32 && c<=126)?c:'?');
        }
    }
    return true;
}
void showStorage(const StorageInfo& info) {
    out("  BusType: "); out(busName(info.bus)); out(" ("); decimal(info.bus); out(")\r\n");
    for (int i=0; i<4; i++) {
        if (!info.fields[i][0]) continue;
        out("  "); out(storageNames[i]); out(": "); out(info.fields[i]); nl();
    }
}
struct ProtocolInfo {
    Proto protocol;
    bool hasGuid;
    U32 errorCode;
};
ProtocolInfo readProtocol(HANDLE h) {
    SffProtocol p={}; p.Size=sizeof(p); U32 got=0;
    BOOL ok=DeviceIoControl(h,IOCTL_SFFDISK_QUERY_DEVICE_PROTOCOL_,nullptr,0,&p,sizeof(p),&got,nullptr);
    if (!ok) return {PROTO_UNKNOWN, false, GetLastError()};
    if (got<sizeof(p)) return {PROTO_UNKNOWN, false, 0};
    Proto proto=equalGuid(p.ProtocolGUID,MMC_GUID)?PROTO_MMC:
                (equalGuid(p.ProtocolGUID,SD_GUID)?PROTO_SD:PROTO_UNKNOWN);
    return {proto, true, 0};
}
void showProtocol(const ProtocolInfo& info) {
    out("  SD/MMC protocol: ");
    if (info.hasGuid) {
        out(info.protocol==PROTO_MMC?"MMC":info.protocol==PROTO_SD?"SD":"unknown GUID");
    } else {
        out("unavailable");
        if (info.errorCode) { out(" (Win32 "); decimal(info.errorCode); out(")"); }
    }
    nl();
}
bool readDiskNumber(HANDLE h, U32& disk) {
    StorageDeviceNumber number={}; U32 got=0;
    if (!DeviceIoControl(h, IOCTL_STORAGE_GET_DEVICE_NUMBER_, nullptr, 0,
                         &number, sizeof(number), &got, nullptr) || got<sizeof(number)) return false;
    if (number.DeviceType!=7 || number.DeviceNumber==0xFFFFFFFFu) return false;
    disk=number.DeviceNumber;
    return true;
}
U64 readDiskLength(HANDLE h) {
    U64 length=0; U32 got=0;
    if (!DeviceIoControl(h, IOCTL_DISK_GET_LENGTH_INFO_, nullptr, 0,
                         &length, sizeof(length), &got, nullptr) || got<sizeof(length)) return 0;
    return length;
}
void showLife(const char* name,U8 v) {
    out("  ");out(name);out(": 0x");hexadecimal(v);out(" ");
    if(v==0) out("undefined");
    else if(v<=10) {decimal((v-1)*10);out("%-");decimal(v*10);out("% estimated lifetime used");}
    else if(v==11) out("estimated lifetime exceeded");
    else out("reserved"); nl();
}
enum class ReadProblem { None, Win32, Length, AllZero };
struct ReadResult {
    ReadProblem problem;
    U32 code;
    U32 received;
    bool ok() const { return problem==ReadProblem::None; }
};
ReadResult readExtCsd(HANDLE h,U8 data[512]) {
    zero(data,512); U32 got=0;
    BOOL ok=DeviceIoControl(h,IOCTL_SFFDISK_MMC_QUERY_EXT_CSD_,nullptr,0,data,512,&got,nullptr);
    if (!ok) return {ReadProblem::Win32, GetLastError(), 0};
    if (got!=512) return {ReadProblem::Length, 0, got};
    for (U32 i=0; i<got; i++) {
        if (data[i]) return {ReadProblem::None, 0, got};
    }
    return {ReadProblem::AllZero, 0, got};
}
void showExtCsd(const U8 data[512], const ReadResult& result) {
    if (!result.ok()) {
        switch (result.problem) {
        case ReadProblem::Win32: error("  MMC_QUERY_EXT_CSD", result.code); break;
        case ReadProblem::Length: out("  EXT_CSD length invalid: "); decimal(result.received); nl(); break;
        default: out("  EXT_CSD all-zero response rejected\r\n"); break;
        }
        return;
    }
    out("  EXT_CSD: 512 bytes received\r\n");
    out("  EXT_CSD_REV [192]: 0x");hexadecimal(data[192]);
    if(data[192]==8) out(" (eMMC 5.1)"); else if(data[192]==7) out(" (eMMC 5.0)");nl();
    U32 sectors=le32(data+212);
    out("  SEC_COUNT [212..215]: ");decimal(sectors);out(" x 512 = ");decimal(U64(sectors)*512u);out(" bytes\r\n");
    if(data[192]>=7) {
        out("  PRE_EOL_INFO [267]: 0x");hexadecimal(data[267]);
        out(data[267]==1?" (normal)\r\n":data[267]==2?" (warning)\r\n":data[267]==3?" (urgent)\r\n":" (undefined/reserved)\r\n");
        showLife("LIFE_TIME_TYPE_A [268]",data[268]);showLife("LIFE_TIME_TYPE_B [269]",data[269]);
    }
}
// SFFDISK data layout is: 24-byte header + 20-byte SDCMD_DESCRIPTOR + DAT read bytes.
enum class SdCommand { Scr, Status, SwitchCheck };
ReadResult sdDataCommand(HANDLE h, SdCommand command, U8* data) {
    const bool isCheck = command==SdCommand::SwitchCheck;
    const U32 n = command==SdCommand::Scr ? 8u : 64u;
    alignas(8) U8 buf[24+20+64];zero(buf,sizeof(buf));
    auto* hdr=reinterpret_cast<SffDeviceCommandData*>(buf);
    hdr->HeaderSize=sizeof(SffDeviceCommandData);
    hdr->Flags=0; hdr->Command=3; // SFFDISK_DC_DEVICE_COMMAND
    hdr->ProtocolArgumentSize=sizeof(SdCmdDescriptor);
    hdr->DeviceDataBufferSize=n;
    hdr->Information=isCheck ? 0x00FFFFFFu : 0; // CMD6 mode 0 (check only)
    auto* desc=reinterpret_cast<SdCmdDescriptor*>(buf+sizeof(SffDeviceCommandData));
    desc->Cmd=isCheck ? 6u : command==SdCommand::Scr ? 51u : 13u;
    desc->CmdClass=isCheck ? 0u : 1u; // SDCC_APP_CMD auto-issues CMD55
    desc->TransferDirection=1; // SDTD_READ
    desc->TransferType=2; // SDTT_SINGLE_BLOCK
    desc->ResponseType=2; // SDRT_1
    U32 got=0;
    const U32 inputs=U32(sizeof(SffDeviceCommandData)+sizeof(SdCmdDescriptor));
    const U32 outputs=inputs+n;
    BOOL ok=DeviceIoControl(h,IOCTL_SFFDISK_DEVICE_COMMAND_,buf,outputs,buf,outputs,&got,nullptr);
    if (!ok) return {ReadProblem::Win32, GetLastError(), 0};
    if (got<outputs) return {ReadProblem::Length, 0, got};
    copy(data,buf+inputs,n);
    return {ReadProblem::None, 0, n};
}
void showBytes(const U8* p,U32 n) {
    for(U32 i=0;i<n;i++) {if(i%16==0) {out("    ");hexadecimal(i,2);out(": ");}
        hexadecimal(p[i]);out(i%16==15 || i==n-1?"\r\n":" ");}
}
// SCR is 8 bytes, transmitted most-significant byte first.
void decodeScr(const U8 scr[8]) {
    out("  SCR_STRUCTURE: ");decimal(scr[0]>>4);nl();
    out("  SD_SPEC: ");decimal(scr[0]&15);nl();
    out("  SD_BUS_WIDTHS: 0x");hexadecimal(scr[1]&15);nl();
    out("  SD_SPEC3: ");decimal((scr[2]>>7)&1);nl();
    out("  SD_SPEC4: ");decimal((scr[2]>>2)&1);nl();
    out("  SD_SPECX: ");decimal(((U32(scr[2])&3)<<2)|(scr[3]>>6));nl();
}
bool makeDumpName(const wchar_t* base,const wchar_t* suffix,wchar_t dest[512]) {
    if(!base) return false;
    U32 i=0;while(base[i] && i<460) {dest[i]=base[i];i++;}
    if(base[i]) return false;
    U32 j=0;while(suffix[j] && i<510) {dest[i++]=suffix[j++];}
    if(suffix[j]) return false;
    dest[i]=0;return true;
}
bool saveData(const wchar_t* filename,const U8* data,U32 n,bool overwrite) {
    if(!filename || !*filename)return false;
    HANDLE h=CreateFileW(filename,WRITE_,SHARE_READ_,nullptr,
                         overwrite?CREATE_ALWAYS_:CREATE_NEW_,ATTR_NORMAL_,nullptr);
    if(!valid(h)) {error("  Dump CreateFileW",GetLastError());return false;}
    U32 wrote=0;BOOL ok=WriteFile(h,data,n,&wrote,nullptr);
    U32 e=ok?0:GetLastError();CloseHandle(h);
    if(!ok || wrote!=n) {error("  Dump WriteFile",ok?1117:e);return false;}
    out("  Binary dump saved (bytes=");decimal(n);out(")\r\n");return true;
}
void dumpWithSuffix(const wchar_t* prefix,const wchar_t* suffix,const U8* data,U32 n,bool overwrite) {
    if(!prefix) return;
    wchar_t name[512];if(!makeDumpName(prefix,suffix,name)) {out("  Dump prefix too long\r\n");return;}
    saveData(name,data,n,overwrite);
}
struct SdRegisters {
    U8 scr[8], status[64], check[64];
    ReadResult scrResult, statusResult, checkResult;
};
bool showSdResult(const char* label, const ReadResult& result, U32 size) {
    if (result.problem==ReadProblem::Win32) {
        error(label, result.code);
    } else if (result.problem==ReadProblem::Length) {
        out(label); out(": incomplete output ("); decimal(result.received);
        out(" bytes; expected >= "); decimal(sizeof(SffDeviceCommandData)+sizeof(SdCmdDescriptor)+size);
        out(")\r\n");
    } else {
        out(label); out(": "); decimal(size); out(" data bytes returned\r\n");
    }
    return result.ok();
}
void showSd(const SdRegisters& sd,const wchar_t* prefix,bool overwrite) {
    out("SD read-only data commands (experimental; requires GENERIC_WRITE handle):\r\n");
    if(showSdResult("  ACMD51 SEND_SCR",sd.scrResult,8)) {
        showBytes(sd.scr,8);decodeScr(sd.scr);dumpWithSuffix(prefix,L"_scr.bin",sd.scr,8,overwrite);
    }
    if(showSdResult("  ACMD13 SD_STATUS",sd.statusResult,64)) {
        showBytes(sd.status,64);
        out("  SPEED_CLASS (SD Status byte 8): ");decimal(sd.status[8]);nl();
        out("  AU_SIZE (byte 10 upper nibble): ");decimal(sd.status[10]>>4);nl();
        dumpWithSuffix(prefix,L"_sd_status.bin",sd.status,64,overwrite);
    }
    if(showSdResult("  CMD6 SWITCH_FUNC check mode",sd.checkResult,64)) {
        showBytes(sd.check,64);dumpWithSuffix(prefix,L"_cmd6_check.bin",sd.check,64,overwrite);
    }
    out("CID/CSD: raw CMD9/CMD10 R2 response extraction is NOT implemented.\r\n");
    out("        Do not interpret successful SFFDISK_DEVICE_COMMAND as a valid 16-byte R2 read.\r\n");
}

struct Target { U32 disk; wchar_t volume; };
void devicePath(Target target, wchar_t path[48]) {
    if (target.volume) volumePath(target.volume, path);
    else diskPath(target.disk, path);
}
struct DeviceInfo {
    Target target;
    bool diskKnown, storageKnown;
    U32 disk;
    U64 bytes;
    StorageInfo storage;
    ProtocolInfo protocol;
};
void readDeviceInfo(HANDLE h, Target target, DeviceInfo& info) {
    info.target=target;
    info.diskKnown=readDiskNumber(h,info.disk);
    info.storageKnown=readStorage(h,info.storage);
    info.protocol=readProtocol(h);
}
struct Inspection {
    DeviceInfo device;
    U32 openError, sdOpenError;
    bool sdReadAttempted, sdProtocolChanged;
    U8 ext[512];
    ReadResult extResult;
    SdRegisters sd;
};
void readInspection(Target target, bool probe, bool structured, Inspection& report) {
    wchar_t path[48]; devicePath(target,path);
    HANDLE h=openDevice(path);
    if (!valid(h)) {
        U32 openError=GetLastError();
        // Inventory and FILE_ANY_ACCESS queries may work on a metadata-only handle.
        if (structured) h=openDevice(path,0);
        if (!valid(h)) {report.openError=openError;return;}
    }

    bool readingSd=false;
    readDeviceInfo(h,target,report.device);
    if (structured) report.device.bytes=readDiskLength(h);
    switch(report.device.protocol.protocol) {
    case PROTO_MMC:
        report.extResult=readExtCsd(h,report.ext);
        break;
    case PROTO_SD:
        readingSd=probe;
        break;
    default:
        break;
    }
    CloseHandle(h);
    if (!readingSd) return;

    // The SD IOCTL requires write access on the handle even for these three reads.
    h=openDevice(path,READ_|WRITE_);
    if (!valid(h)) {report.sdOpenError=GetLastError();return;}
    if (readProtocol(h).protocol==PROTO_SD) {
        report.sdReadAttempted=true;
        report.sd.scrResult=sdDataCommand(h,SdCommand::Scr,report.sd.scr);
        report.sd.statusResult=sdDataCommand(h,SdCommand::Status,report.sd.status);
        report.sd.checkResult=sdDataCommand(h,SdCommand::SwitchCheck,report.sd.check);
    } else {
        report.sdProtocolChanged=true;
    }
    CloseHandle(h);
}
bool showInspection(const Inspection& report,bool probe,const wchar_t* prefix,bool overwrite) {
    if (report.openError) {error("CreateFileW",report.openError);return false;}
    const DeviceInfo& device=report.device;
    if (device.diskKnown) {
        out("  Windows device number: "); decimal(device.disk); nl();
    }
    if (device.storageKnown) showStorage(device.storage);
    showProtocol(device.protocol);
    if(device.protocol.protocol==PROTO_MMC) {
        showExtCsd(report.ext,report.extResult);
        if (!report.extResult.ok()) return false;
        dumpWithSuffix(prefix,L"_extcsd.bin",report.ext,512,overwrite);
    } else if(device.protocol.protocol==PROTO_SD) {
        out("  Native SD card detected. No standard eMMC lifetime fields exist.\r\n");
        if (report.sdOpenError) {
            error("CreateFileW(GENERIC_READ|GENERIC_WRITE)",report.sdOpenError);
            out("  Required for FILE_WRITE_ACCESS IOCTL, even though commands are read-only.\r\n");
            return false;
        }
        if (report.sdReadAttempted) {
            out("  SD/MMC protocol: SD\r\n");
            showSd(report.sd,prefix,overwrite);
        } else if (probe) {
            out("  Refusing SD commands: protocol changed or could not be verified.\r\n");
        } else {
            out("  Use --sd-probe to request read-only SCR, SD Status and CMD6-check.\r\n");
        }
    } else {
        if(device.storage.bus==7) out("  USB transport: SD/MMC raw commands are not exposed by generic USBSTOR.\r\n");
        else out("  No native MMC/SD protocol detected; not issuing MMC/SD commands.\r\n");
    }
    return true;
}

void jsonString(const char* value) {
    char escaped[2*128+3];
    U32 length=0;
    escaped[length++]='"';
    while (*value) {
        if (*value=='"' || *value=='\\') escaped[length++]='\\';
        escaped[length++]=*value++;
    }
    escaped[length++]='"';
    escaped[length]=0;
    out(escaped);
}
void jsonDevice(const DeviceInfo& info) {
    out("{\"disk\":");
    if (!info.target.volume) decimal(info.target.disk);
    else if (info.diskKnown) decimal(info.disk);
    else out("null");
    out(",\"volume\":");
    if (info.target.volume) {
        char volume[]={char(info.target.volume),':',0};
        jsonString(volume);
    } else out("null");
    out(",\"busType\":"); decimal(info.storage.bus);
    out(",\"bus\":"); jsonString(busName(info.storage.bus));
    out(",\"protocol\":");
    jsonString(info.protocol.protocol==PROTO_MMC?"mmc":info.protocol.protocol==PROTO_SD?"sd":"unknown");
    out(",\"protocolError\":"); decimal(info.protocol.errorCode);
    out(",\"sizeBytes\":"); decimal(info.bytes);
    for (int i=0; i<4; i++) {
        out(","); jsonString(storageKeys[i]); out(":"); jsonString(info.storage.fields[i]);
    }
    out("}");
}
void jsonRegister(const ReadResult& result,const U8* data,U32 size) {
    if (result.ok()) {
        const char digits[]="0123456789ABCDEF";
        char hex[1025];
        for (U32 i=0; i<size; i++) {
            hex[2*i]=digits[data[i]>>4];
            hex[2*i+1]=digits[data[i]&15];
        }
        hex[2*size]=0;
        out("{\"hex\":\""); out(hex); out("\"}");
    } else if (result.problem==ReadProblem::Win32) {
        out("{\"issue\":\"win32\",\"code\":"); decimal(result.code); out("}");
    } else if (result.problem==ReadProblem::Length) {
        out("{\"issue\":\"length\",\"received\":"); decimal(result.received); out("}");
    } else {
        out("{\"issue\":\"all-zero\"}");
    }
}
bool jsonInspection(const Inspection& report) {
    if (report.openError) {
        out("{\"openError\":"); decimal(report.openError); out("}\r\n");
        return false;
    }
    out("{\"device\":"); jsonDevice(report.device);
    if (report.sdOpenError) {out(",\"sdOpenError\":"); decimal(report.sdOpenError);}
    if (report.sdProtocolChanged) out(",\"sdProtocolChanged\":true");
    out(",\"registers\":{");
    if (report.device.protocol.protocol==PROTO_MMC) {
        out("\"extCsd\":"); jsonRegister(report.extResult,report.ext,512);
    }
    if (report.sdReadAttempted) {
        out("\"scr\":"); jsonRegister(report.sd.scrResult,report.sd.scr,8);
        out(",\"sdStatus\":"); jsonRegister(report.sd.statusResult,report.sd.status,64);
        out(",\"cmd6\":"); jsonRegister(report.sd.checkResult,report.sd.check,64);
    }
    out("}}\r\n");
    return (report.device.protocol.protocol!=PROTO_MMC || report.extResult.ok()) && !report.sdOpenError;
}

HANDLE openInventory(const wchar_t* path) {
    HANDLE h=openDevice(path,0);
    return valid(h)?h:openDevice(path);
}
void listDisks(bool json) {
    out(json?"{\"devices\":[":"Enumerating PhysicalDrive0..63 (read-only):\r\n");
    U32 total=0;
    for(U32 i=0;i<64;i++) {
        wchar_t path[48];diskPath(i,path);
        HANDLE h=json?openInventory(path):openDevice(path);
        if(!valid(h)) continue;
        DeviceInfo info={};
        readDeviceInfo(h,{i,0},info);
        CloseHandle(h);
        if (json) {
            if (total) out(",");
            jsonDevice(info);
        } else {
            out("\r\nPhysicalDrive"); decimal(i); nl();
            if (info.storageKnown) showStorage(info.storage);
            else out("  StorageDeviceProperty: unavailable\r\n");
            showProtocol(info.protocol);
        }
        total++;
    }
    if (!json) {
        out("\r\nDisks opened: "); decimal(total); nl();
        return;
    }

    // Native SD IOCTLs may be available on a volume handle rather than the physical disk.
    U32 driveMask=GetLogicalDrives();
    for (U32 i=0; i<26; i++) {
        if (!(driveMask & (1u<<i))) continue;
        const wchar_t letter=wchar_t(L'A'+i);
        wchar_t root[]={letter,L':',L'\\',0};
        const U32 driveType=GetDriveTypeW(root);
        if (driveType!=2 && driveType!=3) continue; // DRIVE_REMOVABLE or DRIVE_FIXED
        wchar_t path[48]; volumePath(letter,path);
        HANDLE h=openInventory(path);
        if (!valid(h)) continue;
        DeviceInfo info={};
        readDeviceInfo(h,{0,letter},info);
        CloseHandle(h);
        if (!info.diskKnown && info.protocol.protocol==PROTO_UNKNOWN) continue;
        if (total++) out(",");
        jsonDevice(info);
    }
    out("]}\r\n");
}
bool autoSelect(U32& selected,bool json) {
    U32 matches=0;
    for(U32 i=0;i<64;i++) {
        wchar_t path[48];diskPath(i,path);
        HANDLE h=openDevice(path);if(!valid(h))continue;
        Proto p=readProtocol(h).protocol;CloseHandle(h);
        if(p==PROTO_SD || p==PROTO_MMC) { selected=i;matches++; }
    }
    if(matches!=1) {
        if (json) {out("{\"candidateCount\":"); decimal(matches); out("}\r\n");}
        else {
            out("--auto: native SD/MMC candidates = ");decimal(matches);
            out(" (must be exactly 1). Use --list and --disk N.\r\n");
        }
        return false;
    }
    if (!json) {out("--auto selected PhysicalDrive"); decimal(selected); nl();}
    return true;
}
void help() {
    out("SD/MMC Inspect for Windows x64 -- v3 (read-only by command whitelist)\r\n");
    out("Usage:\r\n");
    out("  sdmmc_inspect_x64.exe\r\n");
    out("  sdmmc_inspect_x64.exe --list\r\n");
    out("  sdmmc_inspect_x64.exe --auto [--dump-prefix PREFIX] [--sd-probe]\r\n");
    out("  sdmmc_inspect_x64.exe --disk N [--dump-prefix PREFIX] [--sd-probe]\r\n");
    out("  sdmmc_inspect_x64.exe --volume C: [--dump-prefix PREFIX] [--sd-probe]\r\n");
    out("\r\nNo arguments open the interactive menu.\r\n");
    out("--json returns structured output and cannot be combined with --dump-prefix.\r\n");
    out("--sd-probe needs an administrator and a native SD card.\r\n");
    out("SD commands are ACMD51 read(8), ACMD13 read(64), CMD6 check mode(64).\r\n");
    out("Never sends eMMC CMD6, CMD1, CMD56, SD CMD6 switch mode or data writes.\r\n");
    out("Dumps PREFIX_extcsd.bin, PREFIX_scr.bin, PREFIX_sd_status.bin,\r\n");
    out("      PREFIX_cmd6_check.bin depending on available features.\r\n");
}

bool inspectTarget(Target target,bool probe,const wchar_t* prefix,bool json,bool overwrite) {
    if (!json) {
        if (target.volume) {
            const char volume[]={char(target.volume),':',0};
            out("Opening volume "); out(volume); nl();
        } else {
            out("Opening PhysicalDrive"); decimal(target.disk); nl();
        }
    }
    Inspection report={};
    readInspection(target,probe,json,report);
    return json?jsonInspection(report):showInspection(report,probe,prefix,overwrite);
}

enum class LineResult { Ok, End, Invalid };
class MenuInput {
    HANDLE handle=GetStdHandle(0xFFFFFFF6u); // STD_INPUT_HANDLE = -10
    U32 mode=0;
    bool console=GetConsoleMode(handle,&mode)!=0;
    bool firstLine=true;
    wchar_t consoleBuffer[128];
    U32 consoleOffset=0, consoleSize=0;

    bool nextChar(U32& value) {
        if (console) {
            if (consoleOffset==consoleSize) {
                consoleOffset=0;
                consoleSize=0;
                if (!ReadConsoleW(handle,consoleBuffer,128,&consoleSize,nullptr) || !consoleSize) return false;
            }
            value=U32(consoleBuffer[consoleOffset++]);
            return true;
        }
        char byte=0;
        U32 received=0;
        if (!ReadFile(handle,&byte,1,&received,nullptr) || !received) return false;
        value=U8(byte);
        return true;
    }

    LineResult readLine(wchar_t (&line)[512]) {
        char utf8[512*4];
        U32 length=0;
        bool invalid=false, hasInput=false;
        for (;;) {
            U32 value=0;
            if (!nextChar(value)) {
                if (!hasInput) return LineResult::End;
                break;
            }
            if (!hasInput && value==0x1A) return LineResult::End; // Ctrl+Z
            hasInput=true;
            if (value=='\n') break;
            if (value=='\r') continue;
            if (!value) {invalid=true; continue;}
            if (console && length<511) line[length++]=wchar_t(value);
            else if (!console && length<sizeof(utf8)) utf8[length++]=char(value);
            else invalid=true;
        }
        if (!console && length && !invalid) {
            int count=MultiByteToWideChar(65001,8,utf8,int(length),line,511); // UTF-8, strict
            if (!count) invalid=true;
            length=U32(count);
        }
        if (invalid) { firstLine=false; return LineResult::Invalid; }
        const U32 start=firstLine && length && line[0]==0xFEFF ? 1u : 0u;
        firstLine=false;
        return trim(line,start,length);
    }

    static LineResult trim(wchar_t (&line)[512],U32 start,U32 length) {
        while (start<length && (line[start]==L' ' || line[start]==L'\t')) start++;
        while (length>start && (line[length-1]==L' ' || line[length-1]==L'\t')) length--;
        for (U32 i=0; i<length-start; i++) line[i]=line[start+i];
        line[length-start]=0;
        return LineResult::Ok;
    }
public:
    bool prompt(const char* label,wchar_t (&line)[512]) {
        for (;;) {
            out(label);
            LineResult result=readLine(line);
            if (result==LineResult::Ok) return true;
            if (result==LineResult::End) {nl();return false;}
            out("Input is too long or invalid. Redirected input must be UTF-8.\r\n");
        }
    }
};

int interactive() {
    MenuInput input;
    wchar_t line[512];
    out("SD/MMC Inspect for Windows x64 -- interactive\r\n");
    for (;;) {
        out("\r\n  1  List physical disks\r\n"
            "  2  Auto-select one native SD/MMC disk\r\n"
            "  3  Inspect a physical disk\r\n"
            "  4  Inspect a drive letter\r\n"
            "  0  Exit\r\n");
        if (!input.prompt("Select an option: ",line) || same(line,L"0")) return 0;
        if (same(line,L"1")) {listDisks(false);continue;}

        Target target={};
        if (same(line,L"2")) {
            if (!autoSelect(target.disk,false)) continue;
        } else if (same(line,L"3")) {
            if (!input.prompt("Physical disk number (0..4095, Enter to cancel): ",line)) return 0;
            if (!line[0]) continue;
            if (!parseDisk(line,target.disk)) {out("Invalid disk number (0..4095)\r\n");continue;}
        } else if (same(line,L"4")) {
            if (!input.prompt("Drive letter (for example E:, Enter to cancel): ",line)) return 0;
            if (!line[0]) continue;
            if (!parseVolume(line,target.volume)) {out("Invalid drive letter\r\n");continue;}
        } else {
            out("Choose 0, 1, 2, 3, or 4.\r\n");
            continue;
        }

        bool probe=false;
        for (;;) {
            if (!input.prompt("Try experimental native-SD register reads? [y/N]: ",line)) return 0;
            if (same(line,L"y") || same(line,L"Y")) {probe=true;break;}
            if (!line[0] || same(line,L"n") || same(line,L"N")) break;
            out("Enter y or n.\r\n");
        }
        for (;;) {
            if (!input.prompt("Dump prefix (existing files are kept, Enter to skip): ",line)) return 0;
            U32 length=0;
            while (line[length]) length++;
            if (length>=2 && line[0]==L'"' && line[length-1]==L'"') {
                for (U32 i=1; i<length-1; i++) line[i-1]=line[i];
                length-=2;
                line[length]=0;
            }
            if (length<=460) break;
            out("Dump prefix too long.\r\n");
        }
        inspectTarget(target,probe,line[0]?line:nullptr,false,false);
    }
}

static int run(int firstOption) {
    int argc=0;wchar_t** argv=CommandLineToArgvW(GetCommandLineW(),&argc);
    if(!argv) {error("CommandLineToArgvW",GetLastError());return 1;}
    if (argc<=firstOption) {
        LocalFree(argv);
        return firstOption==1 ? interactive() : 1;
    }
    enum Mode {LIST,AUTO,DISK,VOLUME} mode=LIST;
    Target target={};
    bool sdProbeFlag=false,json=false;
    const wchar_t* prefix=nullptr;
    for(int i=firstOption;i<argc;i++) {
        if(same(argv[i],L"--help")||same(argv[i],L"-h")) {help();LocalFree(argv);return 0;}
        if(same(argv[i],L"--list")) {mode=LIST;continue;}
        if(same(argv[i],L"--auto")) {mode=AUTO;continue;}
        if(same(argv[i],L"--disk") && i+1<argc) {
            mode=DISK;
            if(!parseDisk(argv[++i],target.disk)) {out("Invalid disk number (0..4095)\r\n");LocalFree(argv);return 1;}
            continue;
        }
        if(same(argv[i],L"--volume") && i+1<argc) {
            mode=VOLUME;
            if (!parseVolume(argv[++i],target.volume)) {out("Invalid drive letter\r\n");LocalFree(argv);return 1;}
            continue;
        }
        if(same(argv[i],L"--sd-probe")) {sdProbeFlag=true;continue;}
        if(same(argv[i],L"--json")) {json=true;continue;}
        if(same(argv[i],L"--dump-prefix") && i+1<argc) {prefix=argv[++i];continue;}
        out("Unknown argument.\r\n");help();LocalFree(argv);return 1;
    }
    if (json && prefix) {
        out("{\"error\":\"--json cannot be combined with --dump-prefix\"}\r\n");
        LocalFree(argv); return 1;
    }
    if(mode==LIST) {listDisks(json);LocalFree(argv);return 0;}
    if(mode==AUTO && !autoSelect(target.disk,json)) {LocalFree(argv);return 2;}
    if(mode!=VOLUME) target.volume=0;
    bool ok=inspectTarget(target,sdProbeFlag,prefix,json,true);
    LocalFree(argv);return ok?0:3;
}
#ifdef SDMMC_EMBEDDED
// The GUI's worker-mode argument precedes the original CLI arguments.
extern "C" int __cdecl sdmmc_run_reader() {return run(2);}
#else
extern "C" void mainCRTStartup() {ExitProcess(U32(run(1)));}
#endif
