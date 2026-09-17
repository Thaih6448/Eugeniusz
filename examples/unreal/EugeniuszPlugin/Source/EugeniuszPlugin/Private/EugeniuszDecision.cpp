#include "EugeniuszDecision.h"
#include "Async/Async.h"
#include "eugeniusz/llama.h"
#include <string>
#include <vector>

UEugeniuszDecision* UEugeniuszDecision::Choose(UObject* WorldContextObject, FString ModelPath,
    FString State, FString Question, TArray<FString> Criteria, bool UseGpu)
{
    auto* Action = NewObject<UEugeniuszDecision>();
    Action->RegisterWithGameInstance(WorldContextObject);
    Action->ModelPathValue = MoveTemp(ModelPath);
    Action->StateValue = MoveTemp(State);
    Action->QuestionValue = MoveTemp(Question);
    Action->CriteriaValue = MoveTemp(Criteria);
    Action->GpuValue = UseGpu;
    return Action;
}

void UEugeniuszDecision::Activate()
{
    TWeakObjectPtr<UEugeniuszDecision> WeakThis(this);
    // Capture values, never access UObjects on the worker thread.
    Async(EAsyncExecution::ThreadPool, [WeakThis, Path = ModelPathValue, State = StateValue,
        Question = QuestionValue, Criteria = CriteriaValue, UseGpu = GpuValue]()
    {
        eg_engine* Engine = nullptr;
        auto Options = eg_llama_options_default();
        Options.context_size = 2048;
        Options.gpu_layers = UseGpu ? 99 : 0;
        char ErrorBuffer[1024]{};
        eg_result Result{};
        FString Error;
        if (eg_llama_create(TCHAR_TO_UTF8(*Path), &Options, &Engine, ErrorBuffer, sizeof(ErrorBuffer)) != EG_OK)
            Error = UTF8_TO_TCHAR(ErrorBuffer);
        else
        {
            std::vector<std::string> Text;
            for (const FString& Criterion : Criteria) Text.emplace_back(TCHAR_TO_UTF8(*Criterion));
            std::vector<const char*> Pointers;
            for (const auto& Criterion : Text) Pointers.push_back(Criterion.c_str());
            const std::string Instructions(TCHAR_TO_UTF8(*Question));
            auto Q = eg_question_default();
            Q.instructions = Instructions.c_str(); Q.criteria = Pointers.data();
            Q.count = static_cast<int32_t>(Pointers.size()); Q.min_confidence = 0.8;
            if (eg_evaluate(Engine, TCHAR_TO_UTF8(*State), &Q, &Result) != EG_OK) Error = UTF8_TO_TCHAR(eg_last_error());
            eg_engine_destroy(Engine);
        }
        AsyncTask(ENamedThreads::GameThread, [WeakThis, Result, Error]()
        {
            if (WeakThis.IsValid())
            {
                WeakThis->Completed.Broadcast(Error.IsEmpty() ? Result.choice : -1, Result.confidence,
                                              !Error.IsEmpty() || Result.abstained != 0, Error);
                WeakThis->SetReadyToDestroy();
            }
        });
    });
}
