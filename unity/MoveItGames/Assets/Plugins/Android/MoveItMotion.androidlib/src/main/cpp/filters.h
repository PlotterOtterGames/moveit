// MediaPipe's landmark filters (mediapipe/util/filtering: low pass, One Euro, relative velocity), with the same
// formulas, so landmarks from the NPU are smoothed exactly like MediaPipe smooths them in VIDEO mode: the games' motion
// rules were tuned on top of that smoothing.
#pragma once

#include <cmath>
#include <cstdint>
#include <cstdlib>
#include <deque>

namespace tundu {

class LowPassFilter {
public:
    explicit LowPassFilter(double alpha = 1.0) : alpha_(alpha) {}
    double Apply(double value) { return ApplyWithAlpha(value, alpha_); }
    double ApplyWithAlpha(double value, double alpha) {
        double result = initialized_ ? alpha * value + (1.0 - alpha) * stored_ : value;
        initialized_ = true;
        raw_ = value;
        stored_ = result;
        return result;
    }
    bool HasLastRawValue() const { return initialized_; }
    double LastRawValue() const { return raw_; }
    void Reset() { initialized_ = false; }

private:
    double alpha_, raw_ = 0, stored_ = 0;
    bool initialized_ = false;
};

class OneEuroFilter {
public:
    OneEuroFilter(double minCutoff, double beta, double derivateCutoff)
        : minCutoff_(minCutoff), beta_(beta), derivateCutoff_(derivateCutoff) {}

    // valueScale scales the speed (MediaPipe passes 1 / the body's size in pixels), so beta is body-relative.
    double Apply(int64_t timestampNs, double valueScale, double value) {
        if (lastNs_ >= timestampNs) return value;
        if (lastNs_ != 0 && timestampNs != 0) frequency_ = 1e9 / double(timestampNs - lastNs_);
        lastNs_ = timestampNs;
        double dvalue = x_.HasLastRawValue() ? (value - x_.LastRawValue()) * valueScale * frequency_ : 0.0;
        double edvalue = dx_.ApplyWithAlpha(dvalue, Alpha(derivateCutoff_));
        double cutoff = minCutoff_ + beta_ * std::fabs(edvalue);
        return x_.ApplyWithAlpha(value, Alpha(cutoff));
    }
    void Reset() {
        x_.Reset();
        dx_.Reset();
        lastNs_ = 0;
        frequency_ = 30.0;
    }

private:
    double Alpha(double cutoff) const {
        double te = 1.0 / frequency_, tau = 1.0 / (2.0 * M_PI * cutoff);
        return 1.0 / (1.0 + tau / te);
    }

    double minCutoff_, beta_, derivateCutoff_, frequency_ = 30.0;
    int64_t lastNs_ = 0;
    LowPassFilter x_, dx_;
};

// MediaPipe's RelativeVelocityFilter (legacy distance mode): smooths the two alignment points the next crop comes from.
class RelativeVelocityFilter {
public:
    RelativeVelocityFilter(size_t windowSize, float velocityScale) : windowSize_(windowSize), velocityScale_(velocityScale) {}

    float Apply(int64_t timestampNs, float valueScale, float value) {
        if (lastNs_ >= timestampNs) return value;
        float alpha;
        if (lastNs_ == -1) {
            alpha = 1.f;
        } else {
            float distance = value * valueScale - lastValue_ * lastValueScale_;
            int64_t duration = timestampNs - lastNs_;
            float cumulativeDistance = distance;
            int64_t cumulativeDuration = duration;
            const int64_t assumedMaxDuration = 1000000000 / 30;
            const int64_t maxCumulativeDuration = int64_t(1 + window_.size()) * assumedMaxDuration;
            for (const auto& el : window_) {
                if (cumulativeDuration + el.duration > maxCumulativeDuration) break;
                cumulativeDistance += el.distance;
                cumulativeDuration += el.duration;
            }
            float velocity = cumulativeDistance / float(cumulativeDuration * 1e-9);
            alpha = 1.f - 1.f / (1.f + velocityScale_ * std::fabs(velocity));
            window_.push_front({distance, duration});
            if (window_.size() > windowSize_) window_.pop_back();
        }
        lastValue_ = value;
        lastValueScale_ = valueScale;
        lastNs_ = timestampNs;
        return float(lowPass_.ApplyWithAlpha(value, alpha));
    }
    void Reset() {
        window_.clear();
        lowPass_.Reset();
        lastNs_ = -1;
    }

private:
    struct Element {
        float distance;
        int64_t duration;
    };
    size_t windowSize_;
    float velocityScale_, lastValue_ = 0, lastValueScale_ = 1;
    int64_t lastNs_ = -1;
    std::deque<Element> window_;
    LowPassFilter lowPass_;
};

}  // namespace tundu
