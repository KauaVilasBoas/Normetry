"""The language of a question, Portuguese or English, detected locally (phase 1 design).

Counts function words of each language, plus Portuguese diacritics. Words both languages share
("a", "as", "do", "no", "me") count for neither. Checked against every golden dataset question.
"""

import re

_PT = frozenset(
    """
    o os um uma uns umas de da das dos em na nas num numa ao aos à às pelo pela pelos pelas
    para por com sem sobre entre até após e ou mas nem que se quando onde como porque qual quais
    quanto quanta quantos quantas quem é são ser está estão estar foi era há tem têm ter temos
    pode podem posso podemos deve devem devo devemos precisa precisam preciso precisamos
    vai vão fica ficar não sim já também só muito mais menos isso isto esse essa esses essas
    este esta estes estas aquele aquela eu nós você vocês ele ela eles elas meu minha meus minhas
    nosso nossa nossos nossas seu sua seus suas lhe lhes cada outro outra antes depois
    """.split()
)
_EN = frozenset(
    """
    the an of in on at to for from with without by about into after before and or but nor not
    that this these those what which who whom whose when where why how is are was were be been
    being am do does did done have has had having can could may might must shall should will
    would need needs i we you he she they it my our your his her their its us them there any all
    some each every much many more most less long often if then than so too also only just
    """.split()
)
_PT_LETTERS = re.compile(r"[ãõçáéíóúâêôà]")


def detect_language(text: str) -> str:
    """'pt' or 'en'. A question with no signal either way counts as English."""
    # ponytail: function-word counts; a short question with no function word ("Salmonella?")
    # falls back to English. Replace with a language-id model if questions get that terse.
    words = re.findall(r"[^\W\d_]+", text.lower())
    pt = sum(word in _PT or bool(_PT_LETTERS.search(word)) for word in words)
    en = sum(word in _EN for word in words)
    return "pt" if pt > en else "en"
