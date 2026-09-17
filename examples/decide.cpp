#include <eugeniusz/llama.hpp>
#include <iostream>
int main(int argc, char **argv) {
    if (argc < 2) { std::cerr << "Usage: eugeniusz_decide model.gguf [gpu_layers]\n"; return 2; }
    try {
        auto options = eg_llama_options_default();
        if (argc > 2) options.gpu_layers = std::stoi(argv[2]);
        auto engine = eugeniusz::load_model(argv[1], options);
        auto result = engine.choice("I was charged twice for my subscription.", "Which team should handle this ticket?",
                                    {"Billing: payments, charges and invoices", "Shipping: deliveries and lost parcels", "Technical: software faults"}, 1, 0.8);
        std::cout << "Choice: " << result.choice << "\nConfidence: " << result.confidence << "\nAbstained: " << result.abstained << '\n';
        for (int i = 0; i < result.count; ++i) std::cout << "P[" << i << "]: " << result.probabilities[i] << '\n';
        auto truth = engine.truth("The parcel arrived yesterday.", "Has the parcel arrived?");
        std::cout << "P(true): " << truth.value << '\n';
        auto score = engine.score("The entire production service is down.", "How severe is this incident?", {"Cosmetic", "Degraded service", "Total outage"});
        std::cout << "Severity (0..2): " << score.value << '\n';
    } catch (const std::exception &e) { std::cerr << e.what() << '\n'; return 1; }
}
