#include <eugeniusz/eugeniusz.hpp>
#include <iostream>
int main() {
    const double logits[] = {4, 0, 4, 0, 4, 0, 4, 0};
    const int32_t labels[] = {0, 0, 0, 1};
    double temperature = 1;
    eugeniusz::check(eg_fit_temperature(logits, labels, 4, 2, &temperature));
    eg_result result{};
    eugeniusz::check(eg_from_logits(EG_CHOICE, logits, 2, temperature, 0.9, &result));
    std::cout << "Synthetic calibration example (not language inference)\nTemperature: " << temperature
              << "\nP(class 0): " << result.probabilities[0] << "\nAbstained: " << result.abstained << '\n';
}
