#ifndef EUGENIUSZ_LLAMA_H
#define EUGENIUSZ_LLAMA_H
#include "eugeniusz.h"
#if defined(_WIN32) && !defined(EG_STATIC)
# ifdef EG_LLAMA_BUILD
#  define EG_LLAMA_API __declspec(dllexport)
# else
#  define EG_LLAMA_API __declspec(dllimport)
# endif
#elif defined(__GNUC__)
# define EG_LLAMA_API __attribute__((visibility("default")))
#else
# define EG_LLAMA_API
#endif
#ifdef __cplusplus
extern "C" {
#endif
typedef struct eg_llama_options {
    uint32_t context_size;
    int32_t threads;
    int32_t gpu_layers; /* 0 = CPU, positive = requested offloaded layers. */
} eg_llama_options;
EG_LLAMA_API eg_llama_options eg_llama_options_default(void);
/* Supports dense Qwen2/Qwen3 and SmolLM2 ChatML GGUF models; no model downloads at runtime.
 * Error buffer is caller-owned. Release the returned engine with eg_engine_destroy. */
EG_LLAMA_API int32_t eg_llama_create(const char *model_path, const eg_llama_options *options,
                                    eg_engine **out, char *error_buffer, uint32_t error_capacity);
/* Optional per-engine task instructions. Copied at creation; UTF-8, nonempty.
 * Role framing and the one-letter output contract remain controlled by the adapter.
 * Existing callers and eg_llama_options retain their ABI. */
EG_LLAMA_API int32_t eg_llama_create_with_system_prompt(const char *model_path, const eg_llama_options *options,
                                    const char *system_prompt, eg_engine **out, char *error_buffer, uint32_t error_capacity);
/* Optional bounded greedy text completion, e.g. a reusable scene plan. Only accepts
 * engines created by this adapter. No partial text is returned on failure; output
 * must have room for UTF-8 plus NUL. The caller keeps the engine alive throughout.
 * This is separate from Choice/Score/Truth, which still generate no answer tokens. */
EG_LLAMA_API int32_t eg_llama_generate(eg_engine *engine, const char *system_prompt, const char *prompt,
                                    uint32_t max_tokens, char *output, uint32_t output_capacity,
                                    char *error_buffer, uint32_t error_capacity);
#ifdef __cplusplus
}
#endif
#endif
