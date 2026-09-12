#ifndef OSC_ENGINE_H
#define OSC_ENGINE_H

/* HeartRateMonitor V1 OSC engine
 * Performance-sensitive hot paths (OSC encoding/sending/receiving/decoding) are implemented in C.
 * Build: gcc -shared -O2 -Wall -o osc_engine.dll osc_engine.c -lws2_32
 */

#ifdef __cplusplus
extern "C" {
#endif

#if defined(_WIN32) && defined(OSC_ENGINE_BUILD)
#define OSC_API __declspec(dllexport)
#else
#define OSC_API
#endif

OSC_API int osc_engine_version(void);

/* ---- Typed arguments ---- */
typedef enum {
    OSC_T_STR   = 1,
    OSC_T_INT32 = 2,
    OSC_T_FLOAT = 3,
    OSC_T_INT64 = 4,
    OSC_T_DOUBLE = 5,
    OSC_T_BLOB  = 6,
    OSC_T_TRUE  = 7,
    OSC_T_FALSE = 8,
    OSC_T_NIL   = 9
} osc_type_t;

typedef struct {
    osc_type_t type;
    union {
        const char* s;   /* OSC_T_STR: UTF-8 string */
        int32_t     i;   /* OSC_T_INT32 */
        float       f;   /* OSC_T_FLOAT */
        int64_t     h;   /* OSC_T_INT64 */
        double      d;   /* OSC_T_DOUBLE */
    } v;
    const unsigned char* blob; /* OSC_T_BLOB data pointer */
    int blob_len;              /* OSC_T_BLOB data length */
} osc_arg_t;

/* Encode one OSC message. Returns the byte count, or -1 when the buffer is too small.
 * Layout matches the legacy C# Osc.Encode: address + ",tags" + arguments (4-byte aligned, big-endian). */
OSC_API int osc_encode_message(const char* address, const osc_arg_t* args, int argc,
                               unsigned char* out, int out_cap);

/* Send via UDP using an internally held, thread-safe socket. Returns 0 on success or -1 on failure. */
OSC_API int osc_engine_send(const char* ip, int port, const unsigned char* data, int len);

/* Fast path: send text directly as an OSC string message. Returns 0 on success or -1 on failure. */
OSC_API int osc_engine_send_text(const char* ip, int port, const char* address, const char* text);

/* VRChat /chatbox/input: text plus two booleans (",sTF" form; T/F have no data section).
 * immediate != 0 sends directly to chat; 0 leaves the text in the input box for Enter confirmation.
 * sound != 0 plays the notification sound. Returns 0 on success or -1 on failure. */
OSC_API int osc_engine_send_chatbox(const char* ip, int port, const char* address,
                                    const char* text, int immediate, int sound);

/* #32: Write typed arguments (VRChat /avatar/parameters values, /chatbox/typing, etc.).
 * bool: ",T"/",F" with no data section; float: ",f" as four big-endian bytes. Returns 0 on success or -1 on failure. */
OSC_API int osc_engine_send_bool(const char* ip, int port, const char* address, int value);
OSC_API int osc_engine_send_float(const char* ip, int port, const char* address, float value);

/* Decode a packet (single message or bundle) into JSON array text:
 *   [{"address":"/x","args":[{"t":"s","v":".."},{"t":"i","v":123},...]}, ...]
 * Returns the number of bytes written, or -1 when the buffer is too small. */
OSC_API int osc_decode_to_json(const unsigned char* data, int len, char* out, int out_cap);

/* ---- Receive thread ---- */
typedef void (*osc_receive_fn)(const unsigned char* data, int len, void* user);

/* Bind a UDP port and start the receive thread, invoking (raw, len, user) for packets. Returns 0 on success or -1 on failure. */
OSC_API int osc_engine_start_receiver(int port, osc_receive_fn cb, void* user);
OSC_API void osc_engine_stop_receiver(void);

#ifdef __cplusplus
}
#endif

#endif /* OSC_ENGINE_H */
