# -*- coding: utf-8 -*-
"""
T9 九键拼音 -> 汉字拼音切分 原型验证
验证: 摇杆 8 向拨动产生的数字串, 能否稳定还原出用户想打的拼音
"""

SYLLABLES = """
a ai an ang ao
ba bai ban bang bao bei ben beng bi bian biao bie bin bing bo bu
ca cai can cang cao ce cen ceng cha chai chan chang chao che chen cheng chi
chong chou chu chua chuai chuan chuang chui chun chuo ci cong cou cu cuan cui cun cuo
da dai dan dang dao de dei den deng di dia dian diao die ding diu dong dou du
duan dui dun duo
e ei en eng er
fa fan fang fei fen feng fo fou fu
ga gai gan gang gao ge gei gen geng gong gou gu gua guai guan guang gui gun guo
ha hai han hang hao he hei hen heng hong hou hu hua huai huan huang hui hun huo
ji jia jian jiang jiao jie jin jing jiong jiu ju juan jue jun
ka kai kan kang kao ke kei ken keng kong kou ku kua kuai kuan kuang kui kun kuo
la lai lan lang lao le lei leng li lia lian liang liao lie lin ling liu long lou
lu luan lun luo lv lve
ma mai man mang mao me mei men meng mi mian miao mie min ming miu mo mou mu
na nai nan nang nao ne nei nen neng ni nian niang niao nie nin ning niu nong nou
nu nuan nuo nv nve
o ou
pa pai pan pang pao pei pen peng pi pian piao pie pin ping po pou pu
qi qia qian qiang qiao qie qin qing qiong qiu qu quan que qun
ran rang rao re ren reng ri rong rou ru rua ruan rui run ruo
sa sai san sang sao se sen seng sha shai shan shang shao she shei shen sheng shi
shou shu shua shuai shuan shuang shui shun shuo si song sou su suan sui sun suo
ta tai tan tang tao te teng ti tian tiao tie ting tong tou tu tuan tui tun tuo
wa wai wan wang wei wen weng wo wu
xi xia xian xiang xiao xie xin xing xiong xiu xu xuan xue xun
ya yan yang yao ye yi yin ying yo yong you yu yuan yue yun
za zai zan zang zao ze zei zen zeng zha zhai zhan zhang zhao zhe zhei zhen zheng
zhi zhong zhou zhu zhua zhuai zhuan zhuang zhui zhun zhuo zi zong zou zu zuan zui
zun zuo
"""

# 手机 T9 键盘布局
KEY_OF = {}
for _c, _g in zip("abcdefghijklmnopqrstuvwxyz",
                  "22233344455566677778889999"):
    KEY_OF[_c] = _g


def to_t9(syllable: str) -> str:
    return "".join(KEY_OF[c] for c in syllable)


SYL_LIST = sorted(set(SYL.strip() for SYL in SYLLABLES.split() if SYL.strip()))

# 高频音节权重: 让切分在歧义时偏向更常见的音节
HOT = {
    "shi": 100, "yi": 100, "bu": 90, "wo": 90, "ni": 85, "hao": 80, "de": 100,
    "le": 90, "you": 85, "zai": 80, "ma": 75, "hen": 70, "zhe": 70, "na": 70,
    "men": 80, "dao": 70, "lai": 70, "qu": 70, "shang": 70, "xia": 70,
    "xian": 70, "jian": 65, "wo": 90, "ta": 75, "ge": 70, "mei": 70,
    "shen": 65, "zen": 60, "zen": 60, "yao": 70, "jiu": 70, "xing": 65,
    "dong": 65, "shi": 100, "hou": 65, "kan": 65, "shuo": 70, "hui": 70,
    "neng": 65, "dui": 65, "ran": 65, "hai": 65, "zhi": 70, "guo": 65,
}


def weight(s: str) -> float:
    return HOT.get(s, 10 + len(s))


# 数字串 -> 音节 索引
CODE2SYL = {}
for s in SYL_LIST:
    CODE2SYL.setdefault(to_t9(s), []).append(s)


def segment(digits: str, topk: int = 3):
    """DP 切分: 返回按 (块数少, 权重高) 排序的若干拼音切分"""
    n = len(digits)
    # best[i] = 前 i 位的最优方案列表
    best = [None] * (n + 1)
    best[0] = [(0.0, 0, [])]  # (score, blocks, path)

    for i in range(1, n + 1):
        cands = []
        for L in range(1, 7):  # 音节最长 6 字母
            j = i - L
            if j < 0 or best[j] is None:
                continue
            code = digits[j:i]
            for syl in CODE2SYL.get(code, []):
                for score, blocks, path in best[j]:
                    cands.append((score + weight(syl), blocks + 1,
                                  path + [syl]))
        if not cands:
            # 该位无法构成音节, 允许单字母占位继续
            code = digits[i - 1:i]
            if best[i - 1] is not None:
                for score, blocks, path in best[i - 1]:
                    cands.append((score - 50, blocks + 1, path + [code]))
        if cands:
            cands.sort(key=lambda x: (x[1], -x[0]))
            best[i] = cands[:40]

    if best[n] is None:
        return []
    out = sorted(best[n], key=lambda x: (x[1], -x[0]))[:topk]
    return [(p, round(sc, 1)) for sc, b, p in out]


def lookup(digits: str, topk: int = 6):
    """数字串 -> 汉字候选: DP 切分出若干拼音, 逐个查词库, 按词频排序"""
    best = {}
    for path, _score in segment(digits, topk=8):
        key = "".join(path)
        for word, freq in PHRASES.get(key, []):
            if word not in best or freq > best[word][0]:
                best[word] = (freq, key)
    hits = sorted(((-f, w, k) for w, (f, k) in best.items()))
    return [(w, k) for _f, w, k in hits[:topk]]


# 迷你词库: 演示用, 正式版应换成搜狗/Rime 词库或自建 SQLite
# key = 拼音连写, value = [(词, 词频)]
PHRASES = {
    "nihao": [("你好", 1000)],
    "nihaoma": [("你好吗", 200)],
    "women": [("我们", 900)],
    "nimen": [("你们", 500)],
    "wo": [("我", 1000), ("握", 30)],
    "ni": [("你", 1000), ("尼", 30)],
    "ta": [("他", 600), ("她", 500), ("它", 200)],
    "hao": [("好", 800), ("号", 200), ("毫", 60)],
    "buhao": [("不好", 300)],
    "haode": [("好的", 500)],
    "xiexie": [("谢谢", 800)],
    "pengyou": [("朋友", 400)],
    "zhidao": [("知道", 500)],
    "buzhidao": [("不知道", 400)],
    "shenme": [("什么", 700)],
    "zenme": [("怎么", 500), ("这么", 400)],
    "weishenme": [("为什么", 300)],
    "zenmeban": [("怎么办", 250)],
    "youxi": [("游戏", 700), ("有戏", 100)],
    "kaishi": [("开始", 500)],
    "jieshu": [("结束", 300)],
    "jiayou": [("加油", 400)],
    "lihai": [("厉害", 400)],
    "keyi": [("可以", 500)],
    "meiyou": [("没有", 500)],
    "meishi": [("没事", 300)],
    "jiushi": [("就是", 400)],
    "xianzai": [("现在", 400)],
    "shijian": [("时间", 300)],
    "yixia": [("一下", 300)],
    "dengyixia": [("等一下", 300)],
    "zheli": [("这里", 300)],
    "nali": [("那里", 250)],
    "zaima": [("在吗", 300)],
    "kuaidian": [("快点", 300)],
    "daye": [("打野", 200)],
    "tuanzhan": [("团战", 200)],
    "tuandui": [("团队", 200)],
    "zhang": [("张", 200), ("长", 200), ("章", 80)],
    "xian": [("先", 400), ("现", 200), ("线", 150), ("西安", 100)],
    "hun": [("混", 100), ("昏", 60)],
    "huo": [("火", 200), ("活", 150)],
    "guo": [("过", 300), ("国", 200), ("果", 100)],
    "gun": [("滚", 150)],
    "xin": [("心", 200), ("新", 200), ("信", 150)],
    "yin": [("因", 150), ("音", 100)],
    "gan": [("干", 200), ("感", 150)],
    "gao": [("高", 200), ("搞", 150)],
    "shoubing": [("手柄", 400)],
    "jianpan": [("键盘", 200)],
    "shuru": [("输入", 200)],
    "diannao": [("电脑", 150)],
    "zaijian": [("再见", 200)],
    "duibuqi": [("对不起", 200)],
    "bangmang": [("帮忙", 150)],
    "yiqi": [("一起", 250)],
    "lai le": [("来了", 200)],
    "laile": [("来了", 250)],
    "zoule": [("走了", 150)],
    "chifan": [("吃饭", 200)],
    "shuijiao": [("睡觉", 150)],
    "mingtian": [("明天", 200)],
    "jintian": [("今天", 200)],
    "wanshang": [("晚上", 200)],
    "zaoshang": [("早上", 150)],
    "niubi": [("牛逼", 300)],
    "ma": [("吗", 400), ("妈", 100)],
    "ba": [("吧", 300), ("把", 150)],
    "a": [("啊", 200)],
    "de": [("的", 1000), ("得", 200), ("地", 150)],
    "le": [("了", 900)],
    "bu": [("不", 800), ("布", 50)],
    "zai": [("在", 600), ("再", 300)],
    "you": [("有", 600), ("又", 200)],
    "hen": [("很", 400)],
    "shang": [("上", 300), ("伤", 100)],
    "xia": [("下", 300), ("吓", 80)],
    "da": [("打", 300), ("大", 400)],
    "xing": [("行", 300), ("型", 80)],
    "buxing": [("不行", 250)],
    "shuo": [("说", 400)],
    "kan": [("看", 300)],
    "lai": [("来", 400)],
    "qu": [("去", 400)],
}

TESTS = [
    ("64426", "nihao 你好"),
    ("96636", "women 我们"),
    ("943943", "xiexie 谢谢"),
    ("94264", "zhang 张/长"),
    ("7364968", "pengyou 朋友"),
    ("486", "hun 混 / huo 火 / guo 过"),
    ("64", "ni 你 / mi 米"),
    ("946", "xin 心 / yin 因"),
    ("426", "hao 好"),
    ("24836", "乱拨, 应无候选"),
    ("436", "hao? / gan? "),
    ("94364", "xiao? yin?"),
]

def dump_cs_data():
    """导出 C# 可直接内联使用的数据"""
    print("/* SYLLABLES */")
    print(" ".join(SYL_LIST))
    print()
    print("/* PHRASES */")
    print(";".join(f"{k}:{','.join(w for w, _f in sorted(v, key=lambda x: -x[1]))}"
                   for k, v in PHRASES.items()))
    print()


if __name__ == "__main__":
    if "--dump" in __import__("sys").argv:
        dump_cs_data()
        raise SystemExit
    print("音节总数:", len(SYL_LIST), " 数字编码:", len(CODE2SYL))
    print("词条数:", sum(len(v) for v in PHRASES.values()))
    print()
    print("== 仅切分(无词库) vs 切分+词库 ==")
    print()
    for digits, want in TESTS:
        raw = segment(digits, topk=3)
        raw_s = " | ".join("".join(p) for p, _ in raw) if raw else "(无)"
        words = lookup(digits, topk=5)
        word_s = " ".join(w for w, _k in words) if words else "(无候选)"
        print(f"{digits:>8} 期望[{want}]")
        print(f"         切分: {raw_s}")
        print(f"         候选: {word_s}")
        print()
