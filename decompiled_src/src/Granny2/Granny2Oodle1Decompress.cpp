// ============================================================================
// Decompiled from bin/granny2.dll (ImageBase 0x50000000):
//   - GrannyDecompressData (0x5001f090)
//   - Oodle1Decompress w/ stops (0x5002f3d0)
//   - Arithmetic Bitstream (0x5003f980 .. 0x5003fb10)
//   - LZ + Adaptive Symbol Models (0x50040db0 .. 0x500468a0)
// ============================================================================
#include <stdint.h>
#include <stdlib.h>
#include <string.h>

typedef struct {
    uint32_t low;       // +0x00
    uint32_t range;     // +0x04
    const uint8_t *ptr; // +0x08
    uint32_t step;      // +0x0c
    uint32_t bit_carry; // +0x10
} ArithBits;

static void arith_init(ArithBits *ab, const uint8_t *src) {
    ab->ptr = src + 1;
    uint32_t b0 = src[0];
    ab->bit_carry = b0 & 1;
    ab->low = b0 >> 1;
    ab->range = 0x80;
    ab->step = 0;
}

static void arith_renorm(ArithBits *ab) {
    uint32_t range = ab->range;
    if (range <= 0x800000) {
        uint32_t low = ab->low;
        uint32_t carry = ab->bit_carry & 1;
        const uint8_t *p = ab->ptr;
        do {
            low = ((low * 2 + carry) << 7) | ((*p) >> 1);
            carry = (*p) & 1;
            p++;
            range <<= 8;
        } while (range <= 0x800000);
        ab->ptr = p;
        ab->bit_carry = carry;
        ab->low = low;
        ab->range = range;
    }
}

static uint32_t arith_peek_shift(ArithBits *ab, uint32_t shift, uint32_t max_val) {
    arith_renorm(ab);
    uint32_t step = ab->range >> shift;
    ab->step = step;
    uint32_t v = ab->low / step;
    return (v < max_val) ? v : (max_val - 1);
}

static void arith_consume(ArithBits *ab, uint32_t cum_low, uint32_t freq, uint32_t total) {
    uint32_t step = ab->step;
    uint32_t sub = step * cum_low;
    ab->low -= sub;
    if (cum_low + freq < total) {
        ab->range = step * freq;
    } else {
        ab->range -= sub;
    }
}

static uint32_t arith_get_uniform(ArithBits *ab, uint32_t max_val) {
    arith_renorm(ab);
    uint32_t range = ab->range;
    uint32_t step = range / max_val;
    uint32_t low = ab->low;
    uint32_t sym = low / step;
    if (sym >= max_val) sym = max_val - 1;
    uint32_t sub = sym * step;
    ab->low = low - sub;
    if (sym + 1 < max_val) {
        ab->range = step;
    } else {
        ab->range = range - sub;
    }
    return sym;
}

static uint32_t arith_get_bits(ArithBits *ab, uint32_t shift, uint32_t max_val) {
    arith_renorm(ab);
    uint32_t range = ab->range;
    uint32_t step = range >> shift;
    uint32_t low = ab->low;
    uint32_t sym = low / step;
    if (sym >= max_val) sym = max_val - 1;
    uint32_t sub = sym * step;
    ab->low = low - sub;
    if (sym + 1 < max_val) {
        ab->range = step;
    } else {
        ab->range = range - sub;
    }
    return sym;
}

typedef struct {
    uint16_t total_freq;       // +0x00
    uint16_t next_rebuild;     // +0x02
    uint16_t rebuild_step;     // +0x04
    uint16_t cur_step;         // +0x06
    uint16_t max_freq;         // +0x08
    uint16_t num_active;       // +0x0a
    uint16_t num_table;        // +0x0c
    uint16_t max_symbols;      // +0x0e
    uint16_t *symbols;         // +0x10
    uint16_t *freqs;           // +0x14
    uint32_t search_depth;     // +0x18
    uint32_t search_steps[13]; // +0x1c .. +0x4c
    uint16_t cum[1];           // +0x50 (variable length)
} Model;

static uint32_t model_size(uint32_t max_sym) {
    uint32_t a = (max_sym + 5) & ~3u;
    return a * 6 + sizeof(Model) + 32;
}

static Model* model_init(void *mem, uint32_t max_val, uint32_t max_sym) {
    uint32_t sz = model_size(max_sym);
    memset(mem, 0, sz);
    Model *m = (Model*)mem;
    uint32_t a = (max_sym + 5) & ~3u;
    uint16_t *freqs = (uint16_t*)((uint8_t*)mem + sizeof(Model) + 16 + a * 2);
    uint16_t *syms  = (uint16_t*)((uint8_t*)freqs + a * 2);
    m->symbols = syms;
    m->freqs = freqs;
    m->cum[1] = 0xc000;
    m->cum[2] = 0xc000;
    m->cum[3] = 0xc000;
    m->cum[4] = 0xc000;
    m->cum[5] = 0xc000;
    m->max_symbols = (uint16_t)max_sym;
    m->total_freq = 4;
    m->cum[0] = 0x8000;
    freqs[0] = 4;
    m->next_rebuild = 8;
    m->cur_step = 4;
    uint32_t mf = max_val << 5;
    if (mf < 0x100) mf = 0x100;
    else if (mf > 0x3b38) mf = 0x3b38;
    m->max_freq = (uint16_t)mf;
    uint32_t rs = max_val * 2;
    if (rs < 0x80) {
        m->rebuild_step = 0x80;
    } else {
        uint32_t lim = (m->max_freq >> 1) - 0x20;
        if (rs >= lim) rs = lim;
        m->rebuild_step = (uint16_t)rs;
    }
    return m;
}

static void model_rescale(Model *m) {
    m->freqs[0] >>= 1;
    m->total_freq = m->freqs[0];
    uint16_t best_freq = 0;
    uint32_t best_idx = 0;
    uint32_t i = 1;
    while (i <= m->num_active) {
        while (m->freqs[i] <= 1) {
            if (i >= m->num_active) {
                m->freqs[i] = 0;
                m->num_active--;
                goto done_loop;
            }
            m->freqs[i] = m->freqs[m->num_active];
            m->freqs[m->num_active] = 0;
            m->symbols[i] = m->symbols[m->num_active];
            m->num_active--;
        }
        m->freqs[i] >>= 1;
        m->total_freq += m->freqs[i];
        if (m->freqs[i] > best_freq) {
            best_freq = m->freqs[i];
            best_idx = i;
        }
        i++;
    }
done_loop:
    if (best_freq != 0 && m->num_active != 0 && best_idx != m->num_active) {
        uint16_t tf = m->freqs[m->num_active];
        m->freqs[m->num_active] = m->freqs[best_idx];
        m->freqs[best_idx] = tf;
        uint16_t ts = m->symbols[m->num_active];
        m->symbols[m->num_active] = m->symbols[best_idx];
        m->symbols[best_idx] = ts;
    }
    if (m->num_active != m->max_symbols && m->freqs[0] == 0) {
        m->freqs[0] = 1;
        m->total_freq++;
    }
}

static void model_rebuild_cum(Model *m) {
    int32_t scale = 0x20000 / (int32_t)m->total_freq;
    uint32_t acc = (((uint32_t)m->freqs[0] * (uint32_t)scale) >> 3) + 0x8000;
    m->cum[0] = 0x8000;
    for (uint32_t i = 1; ; i++) {
        m->cum[i] = (uint16_t)acc;
        if (i > m->num_active) break;
        acc += ((uint32_t)m->freqs[i] * (uint32_t)scale) >> 3;
    }
    uint32_t step = m->cur_step * 2;
    if (step > m->rebuild_step) {
        m->next_rebuild = m->total_freq + m->rebuild_step;
        m->num_table = m->num_active;
        m->cum[m->num_active + 1] = 0xc000;
    } else {
        m->cur_step = (uint16_t)step;
        m->next_rebuild = m->total_freq + (uint16_t)step;
        m->num_table = m->num_active;
        m->cum[m->num_active + 1] = 0xc000;
    }
}

static void model_build_steps(uint32_t *out, uint32_t n) {
    uint32_t steps[16];
    uint32_t cnt = 0;
    uint32_t v = n + 1;
    do {
        v = (v + 1) >> 1;
        steps[cnt++] = v;
    } while (v > 4);
    cnt--;
    memset(out, 0, 14 * sizeof(uint32_t));
    out[0] = cnt;
    out[1] = steps[0];
    if (cnt > 0) {
        for (uint32_t i = 0; i < cnt; i++) {
            out[14 - cnt + i] = steps[1 + i];
        }
    }
}

static uintptr_t model_decode(Model *m, ArithBits *ab) {
    if (m->total_freq >= m->next_rebuild) {
        if (m->next_rebuild >= m->max_freq) {
            model_rescale(m);
        }
        model_rebuild_cum(m);
        uint16_t nt = m->num_table;
        m->cum[nt + 2] = 0xc000;
        m->cum[nt + 3] = 0xc000;
        m->cum[nt + 4] = 0xc000;
        m->cum[nt + 5] = 0xc000;
        m->cum[nt + 6] = 0xc000;
        model_build_steps(&m->search_depth, nt);
    }
    uint32_t target = arith_peek_shift(ab, 14, 0x4000) + 0x8000;
    int32_t idx = (int32_t)m->search_steps[0];
    uint32_t depth = m->search_depth;
    for (uint32_t k = 11 - depth; k < 11; k++) {
        int32_t s = (int32_t)m->search_steps[2 + k];
        if (m->cum[idx] > target) idx -= s;
        else idx += s;
    }
    if (m->cum[idx] > target) idx -= 2;
    else idx += 2;
    if (m->cum[idx] > target) {
        if (m->cum[idx - 1] > target) idx -= 2;
        else idx -= 1;
    } else {
        if (m->cum[idx + 1] <= target) idx += 1;
    }
    uint32_t c_low = m->cum[idx];
    uint32_t c_high = m->cum[idx + 1];
    arith_consume(ab, c_low - 0x8000, c_high - c_low, 0x4000);
    m->freqs[idx]++;
    m->total_freq++;
    if (idx > 0) {
        return (uintptr_t)m->symbols[idx];
    }
    if (m->num_active != m->num_table) {
        if (arith_get_bits(ab, 1, 2) != 0) {
            uint32_t r = arith_get_uniform(ab, m->num_active - m->num_table);
            uint32_t sym_idx = m->num_table + r + 1;
            m->freqs[sym_idx] += 2;
            m->total_freq += 2;
            return (uintptr_t)m->symbols[sym_idx];
        }
    }
    m->num_active++;
    uint32_t na = m->num_active;
    m->freqs[na] += 2;
    m->total_freq += 2;
    if (m->num_active == m->max_symbols && m->freqs[0] != 0) {
        m->total_freq -= m->freqs[0];
        m->freqs[0] = 0;
    }
    return (uintptr_t)&m->symbols[na];
}

int granny_decompress_oodle1(
    const uint8_t *comp, uint32_t comp_len,
    uint8_t *uncomp, uint32_t stop0, uint32_t stop1, uint32_t decomp_len
) {
    ArithBits ab;
    arith_init(&ab, comp + 0x24);
    uint32_t stops[3] = { stop0, stop1, decomp_len };
    uint32_t out_pos = 0;
    static const uint32_t len_table[4] = { 128, 192, 256, 512 };
    for (int phase = 0; phase < 3; phase++) {
        const uint32_t *hdr = (const uint32_t*)(comp + phase * 12);
        uint32_t h0 = hdr[0];
        uint32_t h1 = hdr[1];
        uint32_t h2 = hdr[2];
        uint32_t lit_max = h0 & 0x1ff;
        uint32_t win_max = h0 >> 9;
        uint32_t off_low_max = (win_max < 4) ? (win_max + 1) : 4;
        uint32_t off_mid_max = ((win_max >> 2) < 0x100) ? ((win_max >> 2) + 1) : 0x100;
        uint32_t off_hi_max  = (win_max >> 10) + 1;
        uint32_t sym_cap     = h1 & 0x1ff;

        void *arena = calloc(1, 4 * 1024 * 1024);
        if (!arena) return -1;
        uint8_t *p = (uint8_t*)arena;
        Model *lit_m[4];
        for (int i = 0; i < 4; i++) {
            lit_m[i] = model_init(p, lit_max - 1, sym_cap);
            p += model_size(sym_cap);
        }
        Model *len_m[64];
        for (int g = 0; g < 4; g++) {
            uint32_t g_cap = (h2 >> (24 - g * 8)) & 0xff;
            for (int k = 0; k < 16; k++) {
                len_m[g * 16 + k] = model_init(p, 0x40, g_cap);
                p += model_size(g_cap);
            }
        }
        uint32_t last_cap = h2 & 0xff;
        Model *len_m64 = model_init(p, 0x40, last_cap);
        p += model_size(last_cap);

        Model **off_mid_m = (Model**)calloc(off_hi_max + 1, sizeof(Model*));
        for (uint32_t i = 0; i < off_hi_max; i++) {
            off_mid_m[i] = model_init(p, off_mid_max - 1, off_mid_max);
            p += model_size(off_mid_max);
        }
        Model *off_low_m = model_init(p, off_low_max - 1, off_low_max);
        p += model_size(off_low_max);
        uint32_t hi_cap = (h1 >> 19) + 1;
        Model *off_hi_m = model_init(p, off_hi_max - 1, hi_cap);

        uint32_t cur_win = 0;
        uint32_t prev_len_sym = 0;

        while (out_pos < stops[phase]) {
            Model *lm = (prev_len_sym < 64) ? len_m[prev_len_sym] : len_m64;
            uintptr_t r = model_decode(lm, &ab);
            uint32_t len_sym;
            if (r > 0x10000) {
                len_sym = arith_get_uniform(&ab, 0x41);
                *(uint16_t*)r = (uint16_t)len_sym;
            } else {
                len_sym = (uint32_t)r;
            }
            prev_len_sym = len_sym;
            if (len_sym == 0) {
                Model *m = lit_m[out_pos & 3];
                uintptr_t lr = model_decode(m, &ab);
                uint32_t b;
                if (lr > 0x10000) {
                    b = arith_get_uniform(&ab, lit_max);
                    *(uint16_t*)lr = (uint16_t)b;
                } else {
                    b = (uint32_t)lr;
                }
                uncomp[out_pos++] = (uint8_t)b;
                cur_win++;
            } else {
                uint32_t eff_win = (win_max < cur_win) ? win_max : cur_win;
                uint32_t match_len = (len_sym < 0x3d) ? (len_sym + 1) : len_table[len_sym - 0x3d];

                uintptr_t r_low = model_decode(off_low_m, &ab);
                uint32_t o_low;
                if (r_low > 0x10000) {
                    o_low = arith_get_uniform(&ab, off_low_max);
                    *(uint16_t*)r_low = (uint16_t)o_low;
                } else {
                    o_low = (uint32_t)r_low;
                }

                uintptr_t r_hi = model_decode(off_hi_m, &ab);
                uint32_t o_hi;
                if (r_hi > 0x10000) {
                    o_hi = arith_get_uniform(&ab, (eff_win >> 10) + 1);
                    *(uint16_t*)r_hi = (uint16_t)o_hi;
                } else {
                    o_hi = (uint32_t)r_hi;
                }

                uintptr_t r_mid = model_decode(off_mid_m[o_hi], &ab);
                uint32_t o_mid;
                if (r_mid > 0x10000) {
                    uint32_t lim = (eff_win < 0x400) ? ((eff_win >> 2) + 1) : 0x100;
                    o_mid = arith_get_uniform(&ab, lim);
                    *(uint16_t*)r_mid = (uint16_t)o_mid;
                } else {
                    o_mid = (uint32_t)r_mid;
                }

                uint32_t offset = ((o_hi << 8) + o_mid) * 4 + o_low + 1;
                cur_win += match_len;
                for (uint32_t k = 0; k < match_len; k++) {
                    uncomp[out_pos + k] = uncomp[out_pos - offset + k];
                }
                out_pos += match_len;
            }
        }
        free(off_mid_m);
        free(arena);
    }
    return (int)out_pos;
}
