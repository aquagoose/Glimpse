#ifndef GLIMPSE_AUDIO_H
#define GLIMPSE_AUDIO_H

#ifdef __cplusplus
extern "C" {
#endif

#include "GLP_common.h"
#include <stdint.h>

typedef struct GLP_AudioPlayer GLP_AudioPlayer;

typedef struct GLP_AudioPlayerInfo
{
    uint32_t sampleRate;
} GLP_AudioPlayerInfo;

GLP_EXPORT GLP_Result GLP_CreateAudioPlayer(const GLP_AudioPlayerInfo *info, GLP_AudioPlayer **player);
GLP_EXPORT GLP_Result GLP_DestroyAudioPlayer(GLP_AudioPlayer *player);

#ifdef __cplusplus
}
#endif

#endif
