#pragma once
#include "eugeniusz.hpp"
#include "llama.h"
namespace eugeniusz {
inline Engine load_model(const std::string &path, eg_llama_options options = eg_llama_options_default()) {
    if (path.find('\0') != std::string::npos) throw std::invalid_argument("Embedded NUL in model path");
    char error[1024]{}; eg_engine *engine = nullptr;
    if (eg_llama_create(path.c_str(), &options, &engine, error, sizeof(error)) != EG_OK) throw std::runtime_error(error);
    return Engine(engine);
}
inline Engine load_model(const std::string &path, eg_llama_options options, const std::string &system_prompt) {
    if (path.find('\0') != std::string::npos || system_prompt.find('\0') != std::string::npos)
        throw std::invalid_argument("Embedded NUL in model path or system prompt");
    char error[1024]{}; eg_engine *engine = nullptr;
    if (eg_llama_create_with_system_prompt(path.c_str(), &options, system_prompt.c_str(), &engine, error, sizeof(error)) != EG_OK)
        throw std::runtime_error(error);
    return Engine(engine);
}
inline std::string generate(Engine &engine, const std::string &system_prompt, const std::string &prompt, uint32_t max_tokens = 1024) {
    if (system_prompt.find('\0') != std::string::npos || prompt.find('\0') != std::string::npos)
        throw std::invalid_argument("Embedded NUL in completion input");
    std::string output(65536, '\0'); char error[1024]{};
    if (eg_llama_generate(engine.native_handle(), system_prompt.c_str(), prompt.c_str(), max_tokens,
                          output.data(), static_cast<uint32_t>(output.size()), error, sizeof(error)) != EG_OK)
        throw std::runtime_error(error);
    output.resize(output.find('\0'));
    return output;
}
}
