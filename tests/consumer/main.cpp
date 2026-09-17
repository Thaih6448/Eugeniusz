#include <eugeniusz/eugeniusz.hpp>
#ifdef EXPECT_LLAMA
#include <eugeniusz/llama.hpp>
#endif
int main() {
#ifdef EXPECT_LLAMA
    if (eg_llama_options_default().context_size == 0) return 1;
    if (eg_llama_generate(nullptr, "system", "prompt", 32, nullptr, 0, nullptr, 0) != EG_INVALID_ARGUMENT) return 2;
#endif
    const double logits[] = {0, 1}; eg_result result{};
    return eg_from_logits(EG_CHOICE, logits, 2, 1, 0, &result);
}
