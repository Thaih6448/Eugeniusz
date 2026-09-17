#pragma once
#include "CoreMinimal.h"
#include "Kismet/BlueprintAsyncActionBase.h"
#include "EugeniuszDecision.generated.h"

DECLARE_DYNAMIC_MULTICAST_DELEGATE_FourParams(FEugeniuszCompleted, int32, Choice, double, Confidence, bool, Abstained, const FString&, Error);

UCLASS()
class EUGENIUSZPLUGIN_API UEugeniuszDecision : public UBlueprintAsyncActionBase
{
    GENERATED_BODY()
public:
    UPROPERTY(BlueprintAssignable) FEugeniuszCompleted Completed;

    UFUNCTION(BlueprintCallable, meta=(BlueprintInternalUseOnly="true", WorldContext="WorldContextObject"), Category="Eugeniusz")
    static UEugeniuszDecision* Choose(UObject* WorldContextObject, FString ModelPath, FString State,
                                     FString Question, TArray<FString> Criteria, bool UseGpu = true);
    virtual void Activate() override;
private:
    FString ModelPathValue, StateValue, QuestionValue;
    TArray<FString> CriteriaValue;
    bool GpuValue = true;
};
