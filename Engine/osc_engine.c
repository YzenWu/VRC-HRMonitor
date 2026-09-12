#define OSC_ENGINE_BUILD
#define WIN32_LEAN_AND_MEAN
#include <winsock2.h>
#include <ws2tcpip.h>
#include <windows.h>
#include <stdint.h>
#include <string.h>
#include <stdio.h>
#include <stdarg.h>

#include "osc_engine.h"

static int g_init = 0;

static void ensure_net(void)
{
    if (!g_init) {
        WSADATA wsa;
        if (WSAStartup(MAKEWORD(2, 2), &wsa) == 0) g_init = 1;
    }
}

OSC_API int osc_engine_version(void) { return 100; }

/* ================================================================== Encoding */

static void put32(uint8_t* p, int32_t v)
{
    p[0] = (uint8_t)((uint32_t)v >> 24); p[1] = (uint8_t)((uint32_t)v >> 16);
    p[2] = (uint8_t)((uint32_t)v >> 8);  p[3] = (uint8_t)v;
}

static void put64(uint8_t* p, int64_t v)
{
    for (int i = 0; i < 8; i++) p[i] = (uint8_t)((uint64_t)v >> (56 - 8 * i));
}

static void putf(uint8_t* p, float f) { uint32_t b; memcpy(&b, &f, 4); put32(p, (int32_t)b); }
static void putd(uint8_t* p, double d) { uint64_t b; memcpy(&b, &d, 8); put64(p, (int64_t)b); }

/* OSC string: UTF-8 bytes padded with zeros to a 4-byte boundary, including terminator semantics matching the legacy bytes. */
static int encode_string(uint8_t* out, int cap, int* pos, const char* s)
{
    int len = (int)strlen(s);
    int pad = 4 - (len & 3);          /* 1..4, matching legacy Osc.cs */
    if (*pos + len + pad > cap) return -1;
    memcpy(out + *pos, s, len);
    *pos += len;
    memset(out + *pos, 0, pad);
    *pos += pad;
    return 0;
}

OSC_API int osc_encode_message(const char* address, const osc_arg_t* args, int argc,
                               unsigned char* out, int out_cap)
{
    int pos = 0;
    if (encode_string(out, out_cap, &pos, address) < 0) return -1;

    /* Type tags */
    char tags[128];
    int tl = 0;
    tags[tl++] = ',';
    for (int i = 0; i < argc && tl < (int)sizeof(tags) - 1; i++) {
        switch (args[i].type) {
            case OSC_T_STR:   tags[tl++] = 's'; break;
            case OSC_T_INT32: tags[tl++] = 'i'; break;
            case OSC_T_FLOAT: tags[tl++] = 'f'; break;
            case OSC_T_INT64: tags[tl++] = 'h'; break;
            case OSC_T_DOUBLE: tags[tl++] = 'd'; break;
            case OSC_T_BLOB:  tags[tl++] = 'b'; break;
            case OSC_T_TRUE:  tags[tl++] = 'T'; break;
            case OSC_T_FALSE: tags[tl++] = 'F'; break;
            case OSC_T_NIL:   tags[tl++] = 'N'; break;
            default:          tags[tl++] = 's'; break;
        }
    }
    tags[tl] = 0;
    if (encode_string(out, out_cap, &pos, tags) < 0) return -1;

    /* Arguments */
    for (int i = 0; i < argc; i++) {
        const osc_arg_t* a = &args[i];
        switch (a->type) {
            case OSC_T_INT32:
                if (pos + 4 > out_cap) return -1;
                put32(out + pos, a->v.i); pos += 4; break;
            case OSC_T_FLOAT:
                if (pos + 4 > out_cap) return -1;
                putf(out + pos, a->v.f); pos += 4; break;
            case OSC_T_INT64:
                if (pos + 8 > out_cap) return -1;
                put64(out + pos, a->v.h); pos += 8; break;
            case OSC_T_DOUBLE:
                if (pos + 8 > out_cap) return -1;
                putd(out + pos, a->v.d); pos += 8; break;
            case OSC_T_STR:
                if (encode_string(out, out_cap, &pos, a->v.s ? a->v.s : "") < 0) return -1;
                break;
            case OSC_T_BLOB: {
                int bl = a->blob_len < 0 ? 0 : a->blob_len;
                int p = (4 - (bl & 3)) & 3;
                if (pos + 4 + bl + p > out_cap) return -1;
                put32(out + pos, bl); pos += 4;
                if (bl > 0 && a->blob) { memcpy(out + pos, a->blob, bl); pos += bl; }
                memset(out + pos, 0, p); pos += p;
                break;
            }
            default: break; /* T/F/N have no data */
        }
    }
    return pos;
}

/* ================================================================== Sending */

static SOCKET g_sock = INVALID_SOCKET;
static CRITICAL_SECTION g_send_lock;
static int g_send_lock_init = 0;

static void send_lock_ensure(void)
{
    if (!g_send_lock_init) {
        InitializeCriticalSection(&g_send_lock);
        g_send_lock_init = 1;
    }
}

static int ensure_socket(void)
{
    if (g_sock == INVALID_SOCKET) {
        g_sock = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
        if (g_sock == INVALID_SOCKET) return -1;
    }
    return 0;
}

static int resolve_addr(const char* ip, int port, struct sockaddr_in* out)
{
    memset(out, 0, sizeof(*out));
    out->sin_family = AF_INET;
    out->sin_port = htons((unsigned short)port);
    if (ip && inet_pton(AF_INET, ip, &out->sin_addr) == 1) return 0;
    /* Hostname fallback */
    struct addrinfo hints, *res = NULL;
    memset(&hints, 0, sizeof(hints));
    hints.ai_family = AF_INET;
    hints.ai_socktype = SOCK_DGRAM;
    char sport[16];
    snprintf(sport, sizeof(sport), "%d", port);
    if (getaddrinfo(ip, sport, &hints, &res) != 0 || !res) return -1;
    memcpy(out, res->ai_addr, sizeof(*out));
    freeaddrinfo(res);
    return 0;
}

OSC_API int osc_engine_send(const char* ip, int port, const unsigned char* data, int len)
{
    ensure_net();
    send_lock_ensure();
    struct sockaddr_in addr;
    if (resolve_addr(ip, port, &addr) < 0) return -1;
    EnterCriticalSection(&g_send_lock);
    int r = -1;
    if (ensure_socket() == 0) {
        r = (int)sendto(g_sock, (const char*)data, len, 0, (struct sockaddr*)&addr, sizeof(addr));
    }
    LeaveCriticalSection(&g_send_lock);
    return r >= 0 ? 0 : -1;
}

OSC_API int osc_engine_send_text(const char* ip, int port, const char* address, const char* text)
{
    uint8_t buf[8192];
    osc_arg_t a;
    a.type = OSC_T_STR;
    a.v.s = text;
    a.blob = NULL;
    a.blob_len = 0;
    int n = osc_encode_message(address, &a, 1, buf, (int)sizeof(buf));
    if (n < 0) return -1;
    return osc_engine_send(ip, port, buf, n);
}

OSC_API int osc_engine_send_chatbox(const char* ip, int port, const char* address,
                                    const char* text, int immediate, int sound)
{
    uint8_t buf[8192];
    osc_arg_t a[3];
    memset(a, 0, sizeof(a));
    a[0].type = OSC_T_STR;
    a[0].v.s = text;
    /* VRChat convention: first bool sends immediately (F leaves text for confirmation); second bool enables the notification sound. */
    a[1].type = immediate ? OSC_T_TRUE : OSC_T_FALSE;
    a[2].type = sound ? OSC_T_TRUE : OSC_T_FALSE;
    int n = osc_encode_message(address, a, 3, buf, (int)sizeof(buf));
    if (n < 0) return -1;
    return osc_engine_send(ip, port, buf, n);
}

/* #32: Write typed arguments (VRChat /avatar/parameters values, /chatbox/typing, etc.).
 * bool -> ",T"/",F" with no data section; float -> ",f" as four big-endian bytes. Returns 0 on success or -1 on failure. */
OSC_API int osc_engine_send_bool(const char* ip, int port, const char* address, int value)
{
    uint8_t buf[8192];
    osc_arg_t a;
    memset(&a, 0, sizeof(a));
    a.type = value ? OSC_T_TRUE : OSC_T_FALSE;
    int n = osc_encode_message(address, &a, 1, buf, (int)sizeof(buf));
    if (n < 0) return -1;
    return osc_engine_send(ip, port, buf, n);
}

OSC_API int osc_engine_send_float(const char* ip, int port, const char* address, float value)
{
    uint8_t buf[8192];
    osc_arg_t a;
    memset(&a, 0, sizeof(a));
    a.type = OSC_T_FLOAT;
    a.v.f = value;
    int n = osc_encode_message(address, &a, 1, buf, (int)sizeof(buf));
    if (n < 0) return -1;
    return osc_engine_send(ip, port, buf, n);
}

/* ================================================================== Decoding */

static void append(char* out, int cap, int* o, const char* fmt, ...)
{
    if (*o >= cap - 1) return;
    va_list ap;
    va_start(ap, fmt);
    int n = vsnprintf(out + *o, (size_t)(cap - *o), fmt, ap);
    va_end(ap);
    if (n > 0) {
        if (*o + n >= cap) n = cap - 1 - *o;
        *o += n;
    }
}

static void json_escape(const char* s, char* out, int cap)
{
    int o = 0;
    for (; *s && o < cap - 8; s++) {
        unsigned char c = (unsigned char)*s;
        switch (c) {
            case '"':  out[o++] = '\\'; out[o++] = '"'; break;
            case '\\': out[o++] = '\\'; out[o++] = '\\'; break;
            case '\n': out[o++] = '\\'; out[o++] = 'n'; break;
            case '\r': out[o++] = '\\'; out[o++] = 'r'; break;
            case '\t': out[o++] = '\\'; out[o++] = 't'; break;
            default:
                if (c < 0x20) {
                    if (o + 6 >= cap) { o = cap; break; }
                    o += snprintf(out + o, (size_t)(cap - o), "\\u%04x", c);
                } else {
                    out[o++] = (char)c;
                }
        }
    }
    if (o >= cap) o = cap - 1;
    out[o] = 0;
}

static int32_t read_i32(const unsigned char* d, int* pos)
{
    int32_t v = ((int32_t)d[*pos] << 24) | ((int32_t)d[*pos + 1] << 16)
              | ((int32_t)d[*pos + 2] << 8) | (int32_t)d[*pos + 3];
    *pos += 4;
    return v;
}

static int64_t read_i64(const unsigned char* d, int* pos)
{
    uint64_t v = 0;
    for (int i = 0; i < 8; i++) v = (v << 8) | d[*pos + i];
    *pos += 8;
    return (int64_t)v;
}

static double read_d(const unsigned char* d, int* pos)
{
    uint64_t b = (uint64_t)read_i64(d, pos);
    double v;
    memcpy(&v, &b, 8);
    return v;
}

static float read_f(const unsigned char* d, int* pos)
{
    uint32_t b = (uint32_t)read_i32(d, pos);
    float v;
    memcpy(&v, &b, 4);
    return v;
}

static int read_str32(const unsigned char* d, int end, int* pos, char* out, int ocap)
{
    int s = *pos;
    while (s < end && d[s] != 0) s++;
    int n = s - *pos;
    if (n >= ocap) n = ocap - 1;
    memcpy(out, d + *pos, (size_t)n);
    out[n] = 0;
    *pos = (s + 4) & ~3;
    return n;
}

static const char b64c[] = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

static void b64(const unsigned char* src, int n, char* out, int cap)
{
    int o = 0;
    for (int i = 0; i < n && o + 4 < cap; i += 3) {
        uint32_t v = (uint32_t)src[i] << 16;
        if (i + 1 < n) v |= (uint32_t)src[i + 1] << 8;
        if (i + 2 < n) v |= src[i + 2];
        out[o++] = b64c[(v >> 18) & 63];
        out[o++] = b64c[(v >> 12) & 63];
        out[o++] = (i + 1 < n) ? b64c[(v >> 6) & 63] : '=';
        out[o++] = (i + 2 < n) ? b64c[v & 63] : '=';
    }
    out[o] = 0;
}

/* Decode one message, including nested bundles, and append a JSON object to out. */
static int decode_msg(const unsigned char* d, int end, int* pos, char* out, int cap, int* o)
{
    char addr[512], tags[128];
    if (*pos >= end) return -1;
    read_str32(d, end, pos, addr, (int)sizeof(addr));
    if (strcmp(addr, "#bundle") == 0) {
        *pos += 8; /* timetag */
        append(out, cap, o, "[");
        int first = 1;
        while (*pos < end) {
            int32_t size = read_i32(d, pos);
            if (size <= 0 || *pos + size > end) break;
            int ip = *pos;
            if (!first) append(out, cap, o, ",");
            first = 0;
            decode_msg(d, *pos + size, &ip, out, cap, o);
            *pos += size;
        }
        append(out, cap, o, "]");
        return 0;
    }
    read_str32(d, end, pos, tags, (int)sizeof(tags));
    char es_addr[1024];
    json_escape(addr, es_addr, (int)sizeof(es_addr));
    append(out, cap, o, "{\"address\":\"%s\",\"args\":[", es_addr);
    int first = 1;
    int t = tags[0] == ',' ? 1 : 0;
    for (; tags[t]; t++) {
        if (!first) append(out, cap, o, ",");
        first = 0;
        switch (tags[t]) {
            case 'i': {
                int32_t v = read_i32(d, pos);
                append(out, cap, o, "{\"t\":\"i\",\"v\":%d}", (int)v);
                break;
            }
            case 'f': {
                float v = read_f(d, pos);
                append(out, cap, o, "{\"t\":\"f\",\"v\":%.6g}", (double)v);
                break;
            }
            case 'h': {
                int64_t v = read_i64(d, pos);
                append(out, cap, o, "{\"t\":\"h\",\"v\":%lld}", (long long)v);
                break;
            }
            case 'd': {
                double v = read_d(d, pos);
                append(out, cap, o, "{\"t\":\"d\",\"v\":%.14g}", v);
                break;
            }
            case 's': {
                char s[2048], es[4096];
                read_str32(d, end, pos, s, (int)sizeof(s));
                json_escape(s, es, (int)sizeof(es));
                append(out, cap, o, "{\"t\":\"s\",\"v\":\"%s\"}", es);
                break;
            }
            case 'b': {
                int32_t bl = read_i32(d, pos);
                if (bl < 0 || *pos + bl > end) return -1;
                char b64buf[4096];
                b64(d + *pos, bl, b64buf, (int)sizeof(b64buf));
                *pos += bl + ((4 - (bl & 3)) & 3);
                append(out, cap, o, "{\"t\":\"b\",\"v\":\"%s\"}", b64buf);
                break;
            }
            case 'T': append(out, cap, o, "{\"t\":\"T\"}"); break;
            case 'F': append(out, cap, o, "{\"t\":\"F\"}"); break;
            case 'N': append(out, cap, o, "{\"t\":\"N\"}"); break;
            case 'I': append(out, cap, o, "{\"t\":\"I\"}"); break;
            default:  return -1;
        }
    }
    append(out, cap, o, "]}");
    return 0;
}

OSC_API int osc_decode_to_json(const unsigned char* data, int len, char* out, int out_cap)
{
    if (!out || out_cap <= 0) return -1;
    out[0] = 0;
    int o = 0;
    int pos = 0;
    int is_bundle = len >= 8 && memcmp(data, "#bundle", 7) == 0 && data[7] == 0;
    append(out, out_cap, &o, "[");
    if (is_bundle) {
        pos += 8;
        int first = 1;
        while (pos < len) {
            int32_t size = read_i32(data, &pos);
            if (size <= 0 || pos + size > len) break;
            int ip = pos;
            if (!first) append(out, out_cap, &o, ",");
            first = 0;
            decode_msg(data, pos + size, &ip, out, out_cap, &o);
            pos += size;
        }
    } else {
        decode_msg(data, len, &pos, out, out_cap, &o);
    }
    append(out, out_cap, &o, "]");
    return o;
}

/* ================================================================== Receiving */

static volatile LONG g_recv_running = 0;
static HANDLE g_recv_thread = NULL;
static osc_receive_fn g_cb = NULL;
static void* g_user = NULL;
static SOCKET g_recv_sock = INVALID_SOCKET;

static DWORD WINAPI recv_thread_fn(LPVOID param)
{
    (void)param;
    char buf[65536];
    while (g_recv_running) {
        int r = (int)recv(g_recv_sock, buf, (int)sizeof(buf), 0);
        if (r > 0 && g_cb) g_cb((const unsigned char*)buf, r, g_user);
        else if (r == SOCKET_ERROR && WSAGetLastError() == WSAENOTSOCK) break;
    }
    return 0;
}

OSC_API int osc_engine_start_receiver(int port, osc_receive_fn cb, void* user)
{
    if (g_recv_running) return -1;
    ensure_net();
    SOCKET s = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
    if (s == INVALID_SOCKET) return -1;
    struct sockaddr_in a;
    memset(&a, 0, sizeof(a));
    a.sin_family = AF_INET;
    a.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
    a.sin_port = htons((unsigned short)port);
    if (bind(s, (struct sockaddr*)&a, sizeof(a)) != 0) {
        closesocket(s);
        return -1;
    }
    /* 100 ms receive timeout lets recv return periodically so the thread exits quickly on stop, avoiding a join block over 250 ms. */
    {
        int tmo = 100;
        setsockopt(s, SOL_SOCKET, SO_RCVTIMEO, (const char*)&tmo, sizeof(tmo));
    }
    g_recv_sock = s;
    g_cb = cb;
    g_user = user;
    InterlockedExchange(&g_recv_running, 1);
    g_recv_thread = CreateThread(NULL, 0, recv_thread_fn, NULL, 0, NULL);
    if (!g_recv_thread) {
        InterlockedExchange(&g_recv_running, 0);
        closesocket(s);
        g_recv_sock = INVALID_SOCKET;
        return -1;
    }
    return 0;
}

OSC_API void osc_engine_stop_receiver(void)
{
    InterlockedExchange(&g_recv_running, 0);
    if (g_recv_sock != INVALID_SOCKET) {
        closesocket(g_recv_sock);
        g_recv_sock = INVALID_SOCKET;
    }
    if (g_recv_thread) {
        WaitForSingleObject(g_recv_thread, 2000);
        CloseHandle(g_recv_thread);
        g_recv_thread = NULL;
    }
}
