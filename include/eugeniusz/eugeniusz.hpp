#pragma once
#include "eugeniusz.h"
#include <stdexcept>
#include <string>
#include <utility>
#include <vector>
namespace eugeniusz {
inline void check(int32_t status) { if (status != EG_OK) throw std::runtime_error(eg_last_error()); }
class Engine {
    eg_engine *handle_;
public:
    explicit Engine(eg_engine *handle) : handle_(handle) { if (!handle) throw std::invalid_argument("Engine must not be null"); }
    ~Engine() { eg_engine_destroy(handle_); }
    Engine(const Engine &) = delete;
    Engine &operator=(const Engine &) = delete;
    Engine(Engine &&other) noexcept : handle_(std::exchange(other.handle_, nullptr)) {}
    Engine &operator=(Engine &&other) noexcept {
        if (this != &other) { eg_engine_destroy(handle_); handle_ = std::exchange(other.handle_, nullptr); }
        return *this;
    }
    eg_engine *native_handle() const noexcept { return handle_; }
    eg_result evaluate(const std::string &state, const std::string &instructions,
                       const std::vector<std::string> &criteria, eg_kind kind = EG_CHOICE,
                       double temperature = 1, double min_confidence = 0) {
        if (state.find('\0') != std::string::npos || instructions.find('\0') != std::string::npos)
            throw std::invalid_argument("Embedded NUL in text");
        if (criteria.size() > EG_MAX_OPTIONS) throw std::invalid_argument("At most 26 criteria are supported");
        std::vector<const char *> pointers;
        for (const auto &criterion : criteria) {
            if (criterion.find('\0') != std::string::npos) throw std::invalid_argument("Embedded NUL in criterion");
            pointers.push_back(criterion.c_str());
        }
        auto question = eg_question_default();
        question.kind = kind; question.instructions = instructions.c_str();
        question.criteria = pointers.empty() ? nullptr : pointers.data();
        question.count = static_cast<int32_t>(pointers.size());
        question.temperature = temperature; question.min_confidence = min_confidence;
        eg_result result{};
        check(eg_evaluate(handle_, state.c_str(), &question, &result));
        return result;
    }
    eg_result choice(const std::string &state, const std::string &question, const std::vector<std::string> &options, double temperature = 1, double threshold = 0) {
        return evaluate(state, question, options, EG_CHOICE, temperature, threshold);
    }
    eg_result score(const std::string &state, const std::string &question, const std::vector<std::string> &levels, double temperature = 1, double threshold = 0) {
        return evaluate(state, question, levels, EG_SCORE, temperature, threshold);
    }
    eg_result truth(const std::string &state, const std::string &question, double temperature = 1, double threshold = 0) {
        return evaluate(state, question, {}, EG_TRUTH, temperature, threshold);
    }
};
}
